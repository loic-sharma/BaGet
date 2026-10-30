using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// Remote NuGet repositories (P1): a repository that caches an upstream feed. The upstream is a second in-memory host;
/// the downstream reaches it through <see cref="ForwardingRemoteHttpClientFactory"/>, which counts what it receives and
/// can pretend to be down, and time is a <see cref="ManualTimeProvider"/>.
/// </summary>
public class RemoteRepositoryTests : IDisposable
{
    private const string Remote = "up";
    private const string UpstreamUrl = "http://localhost/v3/index.json";
    private const string Versions = "v3/package/testdata/index.json";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly BaGetApplication _upstream;
    private readonly BaGetApplication _app;
    private readonly ForwardingRemoteHttpClientFactory _wire;
    private readonly ManualTimeProvider _time = new();
    private readonly HttpClient _client;

    public RemoteRepositoryTests(ITestOutputHelper output)
    {
        _upstream = new BaGetApplication(output);
        _wire = new ForwardingRemoteHttpClientFactory(_upstream.CreateClient());
        _app = new BaGetApplication(output, configureServices: services =>
        {
            services.AddSingleton<IRemoteHttpClientFactory>(_wire);
            services.AddSingleton(new RemoteRepositoryState(_time));
        });
        _client = _app.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
        _upstream.Dispose();
    }

    private static RemoteRepositorySettings Settings(Action<RemoteRepositorySettings>? change = null)
    {
        var settings = new RemoteRepositorySettings { Url = UpstreamUrl, MetadataTtlSeconds = 600, NegativeTtlSeconds = 60 };
        change?.Invoke(settings);

        return settings;
    }

    private async Task CreateRemoteAsync(Action<RemoteRepositorySettings>? change = null, string name = Remote)
    {
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
            .CreateAsync(name, RepositoryFormat.NuGet, RepositoryKind.Remote, Settings(change), anonymousRead: true, Cancel);
    }

    private async Task UpdateAsync(Action<RemoteRepositorySettings> change, string name = Remote)
    {
        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();
        var current = (await admin.FindAsync(name, Cancel))!.Settings;
        change(current);
        await admin.UpdateAsync(name, current, clearPassword: false, Cancel);
    }

    private async Task SeedUpstreamAsync()
    {
        using var package = TestResources.GetResourceStream(TestResources.Package);
        await _upstream.AddPackageAsync(package, Cancel);
    }

    private async Task<HttpStatusCode> GetAsync(string path)
    {
        using var response = await _client.GetAsync($"r/{Remote}/{path}", Cancel);

        return response.StatusCode;
    }

    private async Task<int> StoredPackagesAsync(string repository = Remote)
    {
        using var scope = _app.Services.CreateScope();
        var summary = await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().FindAsync(repository, Cancel);

        return summary!.PackageCount;
    }

    private const string Nupkg = "v3/package/testdata/1.2.3/testdata.1.2.3.nupkg";

    [Fact]
    public async Task ARemoteRepositoryIsCreatedWithItsUpstreamAndListedAsRemote()
    {
        await CreateRemoteAsync();

        using var scope = _app.Services.CreateScope();
        var info = await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync(Remote, Cancel);
        Assert.Equal(RepositoryKind.Remote, info!.Kind);

        var detail = await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().FindAsync(Remote, Cancel);
        Assert.Equal(UpstreamUrl, detail!.Settings.Url);
        Assert.Equal(600, detail.Settings.MetadataTtlSeconds);
        Assert.True(detail.Settings.StoreLocally);
    }

    [Fact]
    public async Task ARemoteRepositoryNeedsValidSettingsAndOthersTakeNone()
    {
        using var scope = _app.Services.CreateScope();
        var repositories = scope.ServiceProvider.GetRequiredService<IRepositoryService>();

        await Assert.ThrowsAsync<ArgumentException>(() => repositories.CreateAsync("a", RepositoryFormat.NuGet, RepositoryKind.Remote, Cancel));
        await Assert.ThrowsAsync<ArgumentException>(() => repositories.CreateAsync("a", RepositoryFormat.NuGet, RepositoryKind.Remote, new RemoteRepositorySettings { Url = "ftp://x" }, Cancel));
        await Assert.ThrowsAsync<ArgumentException>(() => repositories.CreateAsync("a", RepositoryFormat.NuGet, RepositoryKind.Local, Settings(), Cancel));
        await Assert.ThrowsAsync<NotSupportedException>(() => repositories.CreateAsync("a", RepositoryFormat.NuGet, RepositoryKind.Virtual, Cancel));
        Assert.Null(await repositories.FindAsync("a", Cancel));
    }

    [Fact]
    public async Task APackageIsFetchedOnFirstDownloadStoredAndServedLocallyAfterwards()
    {
        await SeedUpstreamAsync();
        await CreateRemoteAsync();

        Assert.Equal(0, await StoredPackagesAsync());
        Assert.Equal(HttpStatusCode.OK, await GetAsync(Nupkg));
        Assert.Equal(1, await StoredPackagesAsync());
        Assert.Equal(1, _wire.Count(".nupkg"));

        Assert.Equal(HttpStatusCode.OK, await GetAsync(Nupkg));
        Assert.Equal(1, _wire.Count(".nupkg"));

        // Isolated from the default repository.
        using var response = await _client.GetAsync(Nupkg, Cancel);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await StoredPackagesAsync("default"));
    }

    [Fact]
    public async Task MetadataIsReusedForThePeriodAndAskedAgainAfterIt()
    {
        await SeedUpstreamAsync();
        await CreateRemoteAsync();

        Assert.Equal(HttpStatusCode.OK, await GetAsync(Versions));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(Versions));
        Assert.Equal(1, _wire.Count(Versions));

        _time.Advance(TimeSpan.FromSeconds(599));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(Versions));
        Assert.Equal(1, _wire.Count(Versions));

        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(Versions));
        Assert.Equal(2, _wire.Count(Versions));

        using var scope = _app.Services.CreateScope();
        var cache = await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().GetCacheAsync(Remote, Cancel);
        Assert.True(cache.Status.Hits >= 2);
        Assert.NotNull(cache.Status.LastSuccessUtc);
    }

    [Fact]
    public async Task ZeroPeriodAsksTheUpstreamEveryTime()
    {
        await SeedUpstreamAsync();
        await CreateRemoteAsync(s => s.MetadataTtlSeconds = 0);

        await GetAsync(Versions);
        await GetAsync(Versions);

        Assert.Equal(2, _wire.Count(Versions));
    }

    [Fact]
    public async Task ANotFoundIsRememberedForTheNegativePeriod()
    {
        await CreateRemoteAsync();
        const string Missing = "v3/package/nope/index.json";

        Assert.Equal(HttpStatusCode.NotFound, await GetAsync(Missing));
        Assert.Equal(HttpStatusCode.NotFound, await GetAsync(Missing));
        Assert.Equal(1, _wire.Count(Missing));

        _time.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(HttpStatusCode.NotFound, await GetAsync(Missing));
        Assert.Equal(2, _wire.Count(Missing));

        // The package appears upstream; once the remembered 404 expires it is found.
        using var scope = _app.Services.CreateScope();
        Assert.Equal(1, (await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().GetCacheAsync(Remote, Cancel)).Status.NegativeEntries);
    }

    [Fact]
    public async Task AnUnreachableUpstreamIsNeverRememberedAsNotFound()
    {
        await SeedUpstreamAsync();
        await CreateRemoteAsync();

        _wire.Down = true;
        Assert.Equal(HttpStatusCode.NotFound, await GetAsync(Versions));

        using (var scope = _app.Services.CreateScope())
        {
            var cache = await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().GetCacheAsync(Remote, Cancel);
            Assert.Equal(0, cache.Status.NegativeEntries);
            Assert.NotNull(cache.Status.FailingSinceUtc);
            Assert.DoesNotContain("localhost", cache.Status.LastError);
        }

        _wire.Down = false;
        Assert.Equal(HttpStatusCode.OK, await GetAsync(Versions));

        using var healthy = _app.Services.CreateScope();
        Assert.Null((await healthy.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().GetCacheAsync(Remote, Cancel)).Status.FailingSinceUtc);
    }

    [Fact]
    public async Task StoredPackagesAndExpiredMetadataAreServedWhileTheUpstreamIsDown()
    {
        await SeedUpstreamAsync();
        await CreateRemoteAsync();

        Assert.Equal(HttpStatusCode.OK, await GetAsync(Nupkg));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(Versions));

        _wire.Down = true;
        _time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(HttpStatusCode.OK, await GetAsync(Nupkg));
        Assert.Equal(HttpStatusCode.OK, await GetAsync("v3/registration/testdata/index.json"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(Versions));

        using var scope = _app.Services.CreateScope();
        Assert.True((await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().GetCacheAsync(Remote, Cancel)).Status.StaleServed >= 1);
    }

    [Fact]
    public async Task OfflineModeNeverContactsTheUpstreamButServesWhatIsStored()
    {
        await SeedUpstreamAsync();
        await CreateRemoteAsync();
        Assert.Equal(HttpStatusCode.OK, await GetAsync(Nupkg));
        var before = _wire.Requests.Length;

        await UpdateAsync(s => s.Offline = true);

        Assert.Equal(HttpStatusCode.OK, await GetAsync(Nupkg));
        Assert.Equal(HttpStatusCode.NotFound, await GetAsync("v3/package/other/1.0.0/other.1.0.0.nupkg"));
        Assert.Equal(HttpStatusCode.NotFound, await GetAsync("v3/package/other/index.json"));
        Assert.Equal(before, _wire.Requests.Length);
    }

    [Fact]
    public async Task CredentialsAreSentAsBasicAndNeverStoredInClear()
    {
        await SeedUpstreamAsync();
        await CreateRemoteAsync(s =>
        {
            s.Username = "bob";
            s.Password = "s3cret-token";
        });

        await GetAsync(Versions);

        var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("bob:s3cret-token"));
        Assert.All(_wire.Requests, r => Assert.Equal(expected, r.Authorization));
        Assert.NotEmpty(_wire.Requests);

        using var scope = _app.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<IContext>().Repositories!.AsNoTracking().SingleAsync(r => r.Name == Remote, Cancel);
        Assert.DoesNotContain("s3cret-token", row.Settings);
        Assert.Contains("protectedPassword", row.Settings);

        // Never read back to a page; kept when the form leaves it blank.
        var detail = await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().FindAsync(Remote, Cancel);
        Assert.True(detail!.HasPassword);
        Assert.Null(detail.Settings.Password);
        Assert.Null(detail.Settings.ProtectedPassword);

        await UpdateAsync(s => s.MetadataTtlSeconds = 0);
        var count = _wire.Requests.Length;
        await GetAsync(Versions);
        Assert.All(_wire.Requests.Skip(count), r => Assert.Equal(expected, r.Authorization));
    }

    [Fact]
    public async Task ClearingThePasswordSendsNoCredentialsAgain()
    {
        await SeedUpstreamAsync();
        await CreateRemoteAsync(s =>
        {
            s.Username = "bob";
            s.Password = "pw";
        });

        using (var scope = _app.Services.CreateScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();
            var settings = (await admin.FindAsync(Remote, Cancel))!.Settings;
            settings.Username = null;
            settings.MetadataTtlSeconds = 0;
            await admin.UpdateAsync(Remote, settings, clearPassword: true, Cancel);
            Assert.False((await admin.FindAsync(Remote, Cancel))!.HasPassword);
        }

        var count = _wire.Requests.Length;
        await GetAsync(Versions);
        Assert.All(_wire.Requests.Skip(count), r => Assert.Null(r.Authorization));
    }

    [Fact]
    public async Task TheStoredPasswordIsDroppedWhenTheUpstreamOriginOrUserNameChangesWithoutANewOne()
    {
        await CreateRemoteAsync(s =>
        {
            s.Username = "bob";
            s.Password = "pw";
        });

        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();

        // Same origin (only the path changes): the password stays.
        var settings = (await admin.FindAsync(Remote, Cancel))!.Settings;
        settings.Url = settings.Url!.Replace("index.json", "other/index.json");
        await admin.UpdateAsync(Remote, settings, clearPassword: false, Cancel);
        Assert.True((await admin.FindAsync(Remote, Cancel))!.HasPassword);

        // Another user name: dropped.
        settings = (await admin.FindAsync(Remote, Cancel))!.Settings;
        settings.Username = "mallory";
        await admin.UpdateAsync(Remote, settings, clearPassword: false, Cancel);
        Assert.False((await admin.FindAsync(Remote, Cancel))!.HasPassword);

        // Another origin: dropped.
        settings = (await admin.FindAsync(Remote, Cancel))!.Settings;
        settings.Password = "pw2";
        await admin.UpdateAsync(Remote, settings, clearPassword: false, Cancel);
        Assert.True((await admin.FindAsync(Remote, Cancel))!.HasPassword);
        settings = (await admin.FindAsync(Remote, Cancel))!.Settings;
        settings.Url = "https://attacker.example/index.json";
        await admin.UpdateAsync(Remote, settings, clearPassword: false, Cancel);
        Assert.False((await admin.FindAsync(Remote, Cancel))!.HasPassword);
    }

    [Fact]
    public async Task NothingCanBePushedToARemoteRepository()
    {
        await CreateRemoteAsync();

        using var package = TestPackages.Build("Some.Package", "1.0.0");
        using var request = TestPackages.PushRequest(package, apiKey: null);
        request.RequestUri = new Uri($"r/{Remote}/api/v2/package", UriKind.Relative);
        using var response = await _client.SendAsync(request, Cancel);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await StoredPackagesAsync());

        using var control = TestPackages.Build("Some.Package", "1.0.0");
        using var controlRequest = TestPackages.PushRequest(control, apiKey: null);
        using var ok = await _client.SendAsync(controlRequest, Cancel);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    [Fact]
    public async Task DeployIsNeverGrantedOnARemoteRepositoryNotEvenToAnAdministrator()
    {
        await CreateRemoteAsync();
        using var scope = _app.Services.CreateScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IAccessEvaluator>();
        var info = (await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync(Remote, Cancel))!;
        var admin = new BaGetPrincipal("a", "a", isSuperuser: false, ApiKeyScopes.All, "*", isAdministrator: true);

        Assert.False(await evaluator.IsAllowedAsync(admin, info, null, AccessAction.Deploy, Cancel));
        Assert.False(await evaluator.IsAllowedAsync(BaGetPrincipal.Superuser, info, "Any", AccessAction.Deploy, Cancel));
        Assert.True(await evaluator.IsAllowedAsync(admin, info, null, AccessAction.Read, Cancel));
        Assert.True(await evaluator.IsAllowedAsync(admin, info, "Any", AccessAction.Delete, Cancel));
        Assert.False((await evaluator.ExplainAsync(admin, info, null, Cancel)).Has(AccessAction.Deploy));
        Assert.DoesNotContain(info, await evaluator.RepositoriesWithAsync(admin, AccessAction.Deploy, Cancel));
        Assert.Contains(info, await evaluator.RepositoriesWithAsync(admin, AccessAction.Read, Cancel));

        var access = scope.ServiceProvider.GetRequiredService<IRepositoryAccess>();
        Assert.False(await access.CanWriteAsync(null, info, Cancel));
        Assert.DoesNotContain(info, await ((AccessRepositoryAccess)access).WritableAsync(null, Cancel));
    }

    [Fact]
    public async Task APassThroughRepositoryStreamsFromTheUpstreamAndStoresNothing()
    {
        await SeedUpstreamAsync();
        await CreateRemoteAsync(s => s.StoreLocally = false);

        Assert.Equal(HttpStatusCode.OK, await GetAsync(Versions));
        Assert.Equal(HttpStatusCode.OK, await GetAsync("v3/registration/testdata/index.json"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync("v3/registration/testdata/1.2.3.json"));

        using (var download = await _client.GetAsync($"r/{Remote}/{Nupkg}", Cancel))
        {
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.True((await download.Content.ReadAsByteArrayAsync(Cancel)).Length > 100);
        }

        using (var nuspec = await _client.GetAsync($"r/{Remote}/v3/package/testdata/1.2.3/testdata.nuspec", Cancel))
        {
            Assert.Equal(HttpStatusCode.OK, nuspec.StatusCode);
            Assert.Contains("<id>TestData</id>", await nuspec.Content.ReadAsStringAsync(Cancel));
        }

        Assert.Equal(HttpStatusCode.OK, await GetAsync(Nupkg));
        Assert.Equal(0, await StoredPackagesAsync());
        Assert.True(_wire.Count(".nupkg") >= 2);
        Assert.Equal(HttpStatusCode.NotFound, await GetAsync("v3/package/testdata/9.9.9/testdata.9.9.9.nupkg"));
    }

    [Fact]
    public async Task PurgingTheCacheDeletesStoredPackagesAndRememberedAnswers()
    {
        await SeedUpstreamAsync();
        await CreateRemoteAsync();
        await GetAsync(Nupkg);
        await GetAsync(Versions);

        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();
        var before = await admin.GetCacheAsync(Remote, Cancel);
        Assert.Equal(1, before.PackageCount);
        Assert.Equal(1, before.PackageIdCount);
        Assert.True(before.Status.MetadataEntries > 0);

        var purged = await admin.PurgeAsync(Remote, Cancel, "root");
        Assert.Equal(1, purged.Packages);

        var after = await admin.GetCacheAsync(Remote, Cancel);
        Assert.Equal(0, after.PackageCount);
        Assert.Equal(0, after.Status.MetadataEntries);

        // Fetched again on demand.
        Assert.Equal(HttpStatusCode.OK, await GetAsync(Nupkg));
        Assert.Equal(2, _wire.Count(".nupkg"));
    }

    [Fact]
    public async Task PurgingOnePackageLeavesTheOthers()
    {
        await SeedUpstreamAsync();
        using (var other = TestPackages.Build("Other.Package", "1.0.0"))
        {
            await _upstream.AddPackageAsync(other, Cancel);
        }

        await CreateRemoteAsync();
        await GetAsync(Nupkg);
        await GetAsync("v3/package/other.package/1.0.0/other.package.1.0.0.nupkg");
        Assert.Equal(2, await StoredPackagesAsync());

        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();

        var wrong = await admin.PurgePackageAsync(Remote, "TestData", NuGet.Versioning.NuGetVersion.Parse("9.0.0"), Cancel);
        Assert.Equal(0, wrong.Packages);
        Assert.Equal(2, await StoredPackagesAsync());

        var purged = await admin.PurgePackageAsync(Remote, "TestData", null, Cancel, "root");
        Assert.Equal(1, purged.Packages);
        Assert.Equal(1, await StoredPackagesAsync());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => admin.PurgePackageAsync("nope", "TestData", null, Cancel));
    }

    [Fact]
    public async Task ARemoteRepositoryWithCachedPackagesIsDeletedAfterPurgingIt()
    {
        await SeedUpstreamAsync();
        await CreateRemoteAsync();
        await GetAsync(Nupkg);

        using var scope = _app.Services.CreateScope();
        var repositories = scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>();
        var refused = await Assert.ThrowsAsync<RepositoryNotEmptyException>(() => repositories.DeleteAsync(Remote, Cancel));
        Assert.Contains("Purge", refused.Message);

        await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().PurgeAsync(Remote, Cancel);
        await repositories.DeleteAsync(Remote, Cancel);
        Assert.Null(await repositories.FindAsync(Remote, Cancel));
    }

    [Fact]
    public async Task TestConnectionReportsWhatTheUpstreamAnswers()
    {
        await CreateRemoteAsync();
        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();

        var ok = await admin.TestConnectionAsync(Remote, Settings(), Cancel);
        Assert.True(ok.Success, ok.Message);
        Assert.Contains("V3 service index", ok.Message);
        Assert.Equal(200, ok.StatusCode);

        var missing = await admin.TestConnectionAsync(null, Settings(s => s.Url = "http://localhost/nothing/here"), Cancel);
        Assert.False(missing.Success);
        Assert.Equal(404, missing.StatusCode);

        var notV3 = await admin.TestConnectionAsync(null, Settings(s => s.Url = "http://localhost/"), Cancel);
        Assert.False(notV3.Success);

        var invalid = await admin.TestConnectionAsync(null, new RemoteRepositorySettings { Url = "nope" }, Cancel);
        Assert.False(invalid.Success);

        _wire.Down = true;
        var down = await admin.TestConnectionAsync(null, Settings(), Cancel);
        Assert.False(down.Success);
        Assert.DoesNotContain("localhost", down.Message);

        // Nothing was saved or remembered by testing.
        Assert.Equal(0, (await admin.GetCacheAsync(Remote, Cancel)).Status.MetadataEntries);
    }

    [Fact]
    public async Task TestConnectionWithABlankPasswordUsesTheStoredOne()
    {
        await CreateRemoteAsync(s =>
        {
            s.Username = "bob";
            s.Password = "stored-pw";
        });
        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();

        await admin.TestConnectionAsync(Remote, Settings(s => s.Username = "bob"), Cancel);

        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("bob:stored-pw")), _wire.Requests.Last().Authorization);
    }

    [Fact]
    public async Task CachedPackagesAreBrowsableThroughTheRepositoryAwarePages()
    {
        await SeedUpstreamAsync();
        await CreateRemoteAsync();
        await GetAsync(Nupkg);

        using var home = await _client.GetAsync($"r/{Remote}/", Cancel);
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);

        using var search = await _client.GetAsync($"r/{Remote}/v3/search?q=testdata", Cancel);
        Assert.Contains("TestData", await search.Content.ReadAsStringAsync(Cancel));

        using var page = await _client.GetAsync($"r/{Remote}/packages/TestData", Cancel);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
    }

    [Fact]
    public async Task SettingsThatCannotBeUsedAreRefusedAndTheOldOnesKept()
    {
        await CreateRemoteAsync();
        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();

        var bad = Settings(s => s.MetadataTtlSeconds = -1);
        var e = await Assert.ThrowsAsync<ArgumentException>(() => admin.UpdateAsync(Remote, bad, false, Cancel));
        Assert.Contains("metadata cache period", e.Message);
        Assert.Equal(600, (await admin.FindAsync(Remote, Cancel))!.Settings.MetadataTtlSeconds);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => admin.UpdateAsync("default", Settings(), false, Cancel));
        Assert.Null(await admin.FindAsync("default", Cancel));
    }
}
