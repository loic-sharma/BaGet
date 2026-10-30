using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Database.Sqlite;
using BaGet.Database.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// B7: an install from before repositories existed (schema migrated only up to the migration before
/// <c>AddRepositories</c>, filled with packages, owners, an API key, catalog entries, a validation and an abuse report,
/// plus a <c>packages/…</c> store) is started with the current code. Everything is served unchanged at <c>/</c>, the
/// existing key and owner still push, and no stored file moves. The same database upgrade runs on every other engine
/// in <c>BaGet.Database.Tests</c>.
/// </summary>
public class UpgradeFromPreRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BaGetUpgrade", Guid.NewGuid().ToString("N"));
    private readonly string _store;
    private readonly Dictionary<string, string> _storeBefore;
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public UpgradeFromPreRepositoryTests(ITestOutputHelper output)
    {
        Directory.CreateDirectory(_directory);
        _store = Path.Combine(_directory, "store");
        var connection = "Data Source=" + Path.Combine(_directory, "old.db");

        PreRepositoryDatabase.CreateAsync(
            new SqliteDatabaseContextFactory(), connection, new DatabaseOptions { Type = "Sqlite" }, CancellationToken.None)
            .GetAwaiter().GetResult();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        WriteStore();
        _storeBefore = Snapshot();

        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "Database:ConnectionString", connection },
            { "Storage:Path", _store },
            { "Accounts:Enabled", "true" },
            { "ApiKey", "global-key" },
        });
        _client = _app.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    /// <summary>The pre-upgrade store: <c>packages/{id}/{version}/…</c>, lower-cased, as every earlier version wrote it.</summary>
    private void WriteStore()
    {
        foreach (var (id, version, _) in PreRepositoryDatabase.Packages)
        {
            var folder = Path.Combine(_store, "packages", id.ToLowerInvariant(), version);
            Directory.CreateDirectory(folder);
            using var package = TestPackages.Build(id, version);
            File.WriteAllBytes(Path.Combine(folder, $"{id.ToLowerInvariant()}.{version}.nupkg"), ((MemoryStream)package).ToArray());
            File.WriteAllText(Path.Combine(folder, $"{id.ToLowerInvariant()}.nuspec"), $"<package><metadata><id>{id}</id><version>{version}</version></metadata></package>");
        }
    }

    /// <summary>Relative path to content hash, for every file under the store.</summary>
    private Dictionary<string, string> Snapshot()
        => Directory.EnumerateFiles(_store, "*", SearchOption.AllDirectories).ToDictionary(
            f => Path.GetRelativePath(_store, f).Replace('\\', '/'),
            f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

    private async Task<JsonDocument> JsonAsync(string path)
    {
        var token = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync(path, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
    }

    private async Task<HttpStatusCode> PushAsync(string id, string version, string key)
    {
        using var package = TestPackages.Build(id, version);
        using var request = TestPackages.PushRequest(package, key);
        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    [Fact]
    public async Task TheUpgradeTookEffect()
    {
        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        var token = TestContext.Current.CancellationToken;

        Assert.Empty(await context.Database.GetPendingMigrationsAsync(token));
        var repository = await context.Repositories!.SingleAsync(token);
        Assert.Equal((Repository.DefaultKey, Repository.DefaultName), (repository.Key, repository.Name));
        Assert.All(await context.Packages!.ToListAsync(token), p => Assert.Equal(Repository.DefaultKey, p.RepositoryKey));
    }

    [Fact]
    public async Task EveryPackageIsServedAtTheRootUnchanged()
    {
        var token = TestContext.Current.CancellationToken;

        using var versions = await JsonAsync("v3/package/legacy.package/index.json");
        Assert.Equal(new[] { "1.0.0", "2.0.0" }, versions.RootElement.GetProperty("versions").EnumerateArray().Select(v => v.GetString()));

        // The seeded bytes, not a regenerated file.
        var expected = await File.ReadAllBytesAsync(Path.Combine(_store, "packages", "legacy.package", "1.0.0", "legacy.package.1.0.0.nupkg"), token);
        Assert.Equal(expected, await _client.GetByteArrayAsync("v3/package/legacy.package/1.0.0/legacy.package.1.0.0.nupkg", token));
        Assert.Contains("<id>Legacy.Package</id>", await _client.GetStringAsync("v3/package/legacy.package/1.0.0/legacy.package.nuspec", token));

        using var registration = await JsonAsync("v3/registration/legacy.package/index.json");
        var items = registration.RootElement.GetProperty("items")[0].GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { "1.0.0", "2.0.0" }, items.Select(i => i.GetProperty("catalogEntry").GetProperty("version").GetString()));
        Assert.Equal(new[] { true, false }, items.Select(i => i.GetProperty("catalogEntry").GetProperty("listed").GetBoolean()));
        // 7 seeded per version (two versions) plus the download made above.
        Assert.Equal(7 + 7 + 1, registration.RootElement.GetProperty("totalDownloads").GetInt64());
        Assert.Equal("net8.0", items[0].GetProperty("catalogEntry").GetProperty("dependencyGroups")[0].GetProperty("targetFramework").GetString());

        using var other = await JsonAsync("v3/registration/other.package/index.json");
        Assert.Contains("deprecated before the upgrade", other.RootElement.ToString());

        using var search = await JsonAsync("v3/search?q=");
        Assert.Equal(
            new[] { "Legacy.Package", "Other.Package" },
            search.RootElement.GetProperty("data").EnumerateArray().Select(d => d.GetProperty("id").GetString()).OrderBy(i => i));

        using var autocomplete = await JsonAsync("v3/autocomplete?q=Legacy");
        Assert.Equal(new[] { "Legacy.Package" }, autocomplete.RootElement.GetProperty("data").EnumerateArray().Select(d => d.GetString()));

        var feed = await _client.GetStringAsync("api/v2/FindPackagesById()?id='Legacy.Package'", token);
        Assert.Contains("1.0.0", feed);
        Assert.DoesNotContain("/r/", feed);

        using var index = await JsonAsync("v3/index.json");
        Assert.DoesNotContain("/r/", index.RootElement.ToString());
    }

    [Fact]
    public async Task TheCatalogKeepsItsEntries()
    {
        using var index = await JsonAsync("v3/catalog/index.json");
        Assert.Equal(1, index.RootElement.GetProperty("count").GetInt32());

        using var page = await JsonAsync(index.RootElement.GetProperty("items")[0].GetProperty("@id").GetString()!);
        Assert.Equal(2, page.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task OwnersKeysValidationsAndAbuseReportsSurvive()
    {
        var token = TestContext.Current.CancellationToken;

        // The pre-upgrade key still authenticates; the pre-upgrade owner still owns her ID, nobody else's.
        Assert.Equal(HttpStatusCode.Created, await PushAsync("Legacy.Package", "3.0.0", PreRepositoryDatabase.ApiKey));
        Assert.Equal(HttpStatusCode.Forbidden, await PushAsync("Other.Package", "2.0.0", PreRepositoryDatabase.ApiKey));
        Assert.Equal(HttpStatusCode.Unauthorized, await PushAsync("Legacy.Package", "4.0.0", "bgt_not-a-key"));
        Assert.Equal(HttpStatusCode.Created, await PushAsync("Other.Package", "2.0.0", "global-key"));

        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        var owner = await context.PackageOwners!.SingleAsync(token);
        Assert.Equal(("Legacy.Package", PreRepositoryDatabase.UserName, true), (owner.PackageId, owner.OwnerName, owner.IsVerified));
        Assert.Equal("Legacy.Package", (await context.PackageValidations!.SingleAsync(token)).PackageId);
        Assert.Equal("seeded", (await context.AbuseReports!.SingleAsync(token)).Details);

        // New rows join the default repository.
        Assert.All(await context.Packages!.ToListAsync(token), p => Assert.Equal(Repository.DefaultKey, p.RepositoryKey));
        using var versions = await JsonAsync("v3/package/legacy.package/index.json");
        Assert.Contains("3.0.0", versions.RootElement.ToString());
    }

    [Fact]
    public async Task NoStoredFileMovesAndNewOnesLandBesideThem()
    {
        Assert.Equal(HttpStatusCode.Created, await PushAsync("Legacy.Package", "3.0.0", PreRepositoryDatabase.ApiKey));

        var after = Snapshot();
        foreach (var (path, hash) in _storeBefore)
        {
            Assert.True(after.TryGetValue(path, out var current), $"{path} is gone.");
            Assert.Equal(hash, current);
        }

        Assert.Contains("packages/legacy.package/3.0.0/legacy.package.3.0.0.nupkg", after.Keys);
        Assert.DoesNotContain(Directory.EnumerateDirectories(_store).Select(Path.GetFileName), n => n == "repos");
    }

    [Fact]
    public async Task ASecondRepositoryAfterTheUpgradeStartsEmptyAndLeavesTheDefaultAlone()
    {
        var token = TestContext.Current.CancellationToken;
        await _app.CreateRepositoryAsync("second", token);

        using var inSecond = await _client.GetAsync("r/second/v3/package/legacy.package/index.json", token);
        Assert.Equal(HttpStatusCode.NotFound, inSecond.StatusCode);

        using var package = TestPackages.Build("Legacy.Package", "1.0.0");
        using var push = TestPackages.PushRequest(package, "global-key");
        push.RequestUri = new Uri("r/second/api/v2/package", UriKind.Relative);
        using var pushed = await _client.SendAsync(push, token);
        Assert.Equal(HttpStatusCode.Created, pushed.StatusCode);

        // The same ID and version now exists twice; the original still serves its own bytes.
        Assert.Contains("repos/second/packages/legacy.package/1.0.0/legacy.package.1.0.0.nupkg", Snapshot().Keys);
        var original = await File.ReadAllBytesAsync(Path.Combine(_store, "packages", "legacy.package", "1.0.0", "legacy.package.1.0.0.nupkg"), token);
        Assert.Equal(original, await _client.GetByteArrayAsync("v3/package/legacy.package/1.0.0/legacy.package.1.0.0.nupkg", token));
        Assert.Equal(_storeBefore["packages/legacy.package/1.0.0/legacy.package.1.0.0.nupkg"], Snapshot()["packages/legacy.package/1.0.0/legacy.package.1.0.0.nupkg"]);
    }
}

/// <summary>
/// B7 with <c>Mirror:Enabled</c>: an install from before repositories that already mirrored packages into its single
/// repository is started with the current code, which turns the mirror into the <c>mirror</c> remote repository the
/// default one fetches through. <c>/</c> serves exactly as before, what was mirrored stays in <c>default</c> (and is not
/// asked of the upstream again), the <c>mirror</c> repository appears, a new package is still fetched into
/// <c>default</c>, and no stored file moves.
/// </summary>
public class UpgradeFromPreRepositoryMirrorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BaGetUpgrade", Guid.NewGuid().ToString("N"));
    private readonly string _store;
    private readonly Dictionary<string, string> _storeBefore;
    private readonly BaGetApplication _upstream;
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public UpgradeFromPreRepositoryMirrorTests(ITestOutputHelper output)
    {
        Directory.CreateDirectory(_directory);
        _store = Path.Combine(_directory, "store");
        var connection = "Data Source=" + Path.Combine(_directory, "old.db");

        PreRepositoryDatabase.CreateAsync(
            new SqliteDatabaseContextFactory(), connection, new DatabaseOptions { Type = "Sqlite" }, CancellationToken.None)
            .GetAwaiter().GetResult();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var (id, version, _) in PreRepositoryDatabase.Packages)
        {
            var folder = Path.Combine(_store, "packages", id.ToLowerInvariant(), version);
            Directory.CreateDirectory(folder);
            using var package = TestPackages.Build(id, version);
            File.WriteAllBytes(Path.Combine(folder, $"{id.ToLowerInvariant()}.{version}.nupkg"), ((MemoryStream)package).ToArray());
        }

        _storeBefore = Snapshot();

        // The upstream has the package the old install had already mirrored, and one it never fetched.
        _upstream = new BaGetApplication(output);
        using (var mirrored = TestPackages.Build("Legacy.Package", "1.0.0"))
        {
            _upstream.AddPackageAsync(mirrored, CancellationToken.None).GetAwaiter().GetResult();
        }

        using (var fresh = TestResources.GetResourceStream(TestResources.Package))
        {
            _upstream.AddPackageAsync(fresh, CancellationToken.None).GetAwaiter().GetResult();
        }

        _app = new BaGetApplication(output, _upstream.CreateClient(), new Dictionary<string, string>
        {
            { "Database:ConnectionString", connection },
            { "Storage:Path", _store },
            { "Accounts:Enabled", "true" },
            { "ApiKey", "global-key" },
        });
        _client = _app.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
        _upstream.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private Dictionary<string, string> Snapshot()
        => Directory.EnumerateFiles(_store, "*", SearchOption.AllDirectories).ToDictionary(
            f => Path.GetRelativePath(_store, f).Replace('\\', '/'),
            f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

    [Fact]
    public async Task TheMirrorBecomesARemoteRepositoryAndTheMirroredPackagesStayInDefault()
    {
        var token = TestContext.Current.CancellationToken;

        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        var repositories = await context.Repositories!.AsNoTracking().ToListAsync(token);

        Assert.Equal((Repository.DefaultKey, Repository.DefaultName), repositories.Where(r => r.Kind == RepositoryKind.Local).Select(r => (r.Key, r.Name!)).Single());
        var mirror = repositories.Single(r => r.Name == Repository.MirrorName);
        Assert.Equal(RepositoryKind.Remote, mirror.Kind);
        Assert.NotEqual(Repository.DefaultKey, mirror.Key);

        // Every pre-upgrade row is still in default; the mirror repository owns none.
        Assert.All(await context.Packages!.AsNoTracking().IgnoreQueryFilters().ToListAsync(token), p => Assert.Equal(Repository.DefaultKey, p.RepositoryKey));
        Assert.Equal(0, await context.Packages!.IgnoreQueryFilters().CountAsync(p => p.RepositoryKey == mirror.Key, token));
    }

    [Fact]
    public async Task TheRootServesAsBeforeAndTheMirroredPackageIsNotFetchedAgain()
    {
        var token = TestContext.Current.CancellationToken;
        var wire = (ForwardingRemoteHttpClientFactory)_app.Services.GetRequiredService<IRemoteHttpClientFactory>();

        var expected = await File.ReadAllBytesAsync(Path.Combine(_store, "packages", "legacy.package", "1.0.0", "legacy.package.1.0.0.nupkg"), token);
        Assert.Equal(expected, await _client.GetByteArrayAsync("v3/package/legacy.package/1.0.0/legacy.package.1.0.0.nupkg", token));
        Assert.Equal(0, wire.Count(".nupkg"));

        using var response = await _client.GetAsync("v3/package/legacy.package/index.json", token);
        var versions = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token)).RootElement.GetProperty("versions").EnumerateArray().Select(v => v.GetString()).ToList();
        Assert.Contains("1.0.0", versions);
        Assert.Contains("2.0.0", versions);

        using var index = await _client.GetAsync("v3/index.json", token);
        Assert.DoesNotContain("/r/", await index.Content.ReadAsStringAsync(token));
    }

    [Fact]
    public async Task APackageNeverMirroredIsStillFetchedIntoDefaultAndNothingMoves()
    {
        var token = TestContext.Current.CancellationToken;

        using (var response = await _client.GetAsync("v3/package/testdata/1.2.3/testdata.1.2.3.nupkg", token))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var scope = _app.Services.CreateScope();
        var summaries = await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().ListAsync(token);
        Assert.Equal(1, await CountAsync(scope, "TestData", Repository.DefaultKey));
        Assert.Equal(0, summaries.Single(s => s.Name == Repository.MirrorName).PackageCount);

        var after = Snapshot();
        foreach (var (path, hash) in _storeBefore)
        {
            Assert.True(after.TryGetValue(path, out var current), $"{path} is gone.");
            Assert.Equal(hash, current);
        }

        Assert.Contains("packages/testdata/1.2.3/testdata.1.2.3.nupkg", after.Keys);
        Assert.DoesNotContain(Directory.EnumerateDirectories(_store).Select(Path.GetFileName), n => n == "repos");
    }

    private static async Task<int> CountAsync(IServiceScope scope, string id, int repositoryKey)
        => await scope.ServiceProvider.GetRequiredService<IContext>().Packages!.IgnoreQueryFilters()
            .CountAsync(p => p.Id == id && p.RepositoryKey == repositoryKey, TestContext.Current.CancellationToken);
}
