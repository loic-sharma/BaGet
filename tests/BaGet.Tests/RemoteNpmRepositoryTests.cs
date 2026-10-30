using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// Remote npm repositories (P3): a repository that caches an upstream registry. The upstream is a <see cref="FakeNpmUpstream"/>
/// in memory, reached through <see cref="ForwardingRemoteHttpClientFactory"/> (which counts what it receives and can pretend to be
/// down); time is a <see cref="ManualTimeProvider"/>. The real npm client against a real upstream is in <c>BaGet.Clients.Tests</c>.
/// </summary>
public class RemoteNpmRepositoryTests : IDisposable
{
    private const string Key = "secret-key";
    private const string Remote = "up";
    private const string Registry = "/r/up/npm/";
    private const string UpstreamUrl = "http://registry.test/";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly FakeNpmUpstream _upstream = new();
    private readonly ForwardingRemoteHttpClientFactory _wire;
    private readonly ManualTimeProvider _time = new();
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public RemoteNpmRepositoryTests(ITestOutputHelper output)
    {
        _wire = new ForwardingRemoteHttpClientFactory(new HttpClient(_upstream));
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string> { { "ApiKey", Key } },
            configureServices: services =>
            {
                services.AddSingleton<IRemoteHttpClientFactory>(_wire);
                services.AddSingleton(new RemoteRepositoryState(_time));
            });
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }

    private static RemoteRepositorySettings Settings(Action<RemoteRepositorySettings>? change = null)
    {
        // The form of an npm repository posts the NuGet default protocol; the repository's format decides.
        var settings = new RemoteRepositorySettings { Url = UpstreamUrl, Protocol = RemoteProtocol.V3, MetadataTtlSeconds = 600, NegativeTtlSeconds = 60 };
        change?.Invoke(settings);

        return settings;
    }

    private async Task CreateRemoteAsync(Action<RemoteRepositorySettings>? change = null, string name = Remote)
    {
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
            .CreateAsync(name, RepositoryFormat.Npm, RepositoryKind.Remote, Settings(change), anonymousRead: true, Cancel);
    }

    private async Task UpdateAsync(Action<RemoteRepositorySettings> change, string name = Remote)
    {
        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();
        var current = (await admin.FindAsync(name, Cancel))!.Settings;
        change(current);
        await admin.UpdateAsync(name, current, clearPassword: false, Cancel);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? body = null, string? accept = null, string? token = null)
    {
        using var request = NpmTestPackages.Json(method, path, body, token);
        if (accept != null) request.Headers.Accept.ParseAdd(accept);

        return await _client.SendAsync(request, Cancel);
    }

    private async Task<(HttpStatusCode Status, JsonNode? Body)> PackumentAsync(string name, string? accept = null)
    {
        using var response = await SendAsync(HttpMethod.Get, NpmTestPackages.Path(Registry, name), accept: accept);
        var text = await response.Content.ReadAsStringAsync(Cancel);

        return (response.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text));
    }

    private static string FileOf(string name, string version) => $"{(name.Contains('/') ? name.Substring(name.IndexOf('/') + 1) : name)}-{version}.tgz";

    private async Task<HttpResponseMessage> TarballAsync(string name, string version)
        => await SendAsync(HttpMethod.Get, Registry + name + "/-/" + FileOf(name, version));

    // What the repository itself stored (rows and files), whatever its upstream says.
    private async Task<NpmPackageInfo?> StoredAsync(string name, string repository = Remote)
    {
        using var scope = await _app.CreateRepositoryScopeAsync(repository, Cancel);

        return await scope.ServiceProvider.GetRequiredService<NpmRegistryService>().GetPackageAsync(name, Cancel);
    }

    private async Task<int> StoredPackagesAsync(string repository = Remote)
    {
        using var scope = _app.Services.CreateScope();

        return (await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().FindAsync(repository, Cancel))!.PackageCount;
    }

    // ---- creation ----

    [Fact]
    public async Task ARemoteNpmRepositoryIsCreatedWithTheNpmProtocolWhateverTheFormSent()
    {
        await CreateRemoteAsync();

        using var scope = _app.Services.CreateScope();
        var info = await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync(Remote, Cancel);
        Assert.Equal(RepositoryKind.Remote, info!.Kind);
        Assert.Equal(RepositoryFormat.Npm, info.Format);

        var detail = await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().FindAsync(Remote, Cancel);
        Assert.Equal(RemoteProtocol.Npm, detail!.Settings.Protocol);
        Assert.Equal(UpstreamUrl, detail.Settings.Url);
    }

    [Fact]
    public async Task ASavedRemoteNpmRepositoryKeepsTheNpmProtocol()
    {
        await CreateRemoteAsync();

        await UpdateAsync(s =>
        {
            s.Protocol = RemoteProtocol.V2;
            s.MetadataTtlSeconds = 5;
        });

        using var scope = _app.Services.CreateScope();
        var detail = await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().FindAsync(Remote, Cancel);
        Assert.Equal(RemoteProtocol.Npm, detail!.Settings.Protocol);
        Assert.Equal(5, detail.Settings.MetadataTtlSeconds);
    }

    [Fact]
    public async Task ATarballFileNameWithOtherBuildMetadataIsNotFoundAndCachesNothing()
    {
        _upstream.Add("meta-pkg", "1.0.0");
        await CreateRemoteAsync();

        using var other = await TarballAsync("meta-pkg", "1.0.0+x");
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Null(await StoredAsync("meta-pkg"));

        // The real URL still works and is cached under the upstream's spelling.
        using var real = await TarballAsync("meta-pkg", "1.0.0");
        Assert.Equal(HttpStatusCode.OK, real.StatusCode);
        Assert.Equal("1.0.0", Assert.Single((await StoredAsync("meta-pkg"))!.Versions).Version);
    }

    [Fact]
    public async Task AnUpstreamListingVersionsThatDifferOnlyInCaseServesEachByItsOwnSpelling()
    {
        _upstream.Add("case-pkg", "1.0.0-Beta", "1.0.0-beta");
        await CreateRemoteAsync();

        using var lower = await TarballAsync("case-pkg", "1.0.0-beta");
        Assert.Equal(HttpStatusCode.OK, lower.StatusCode);
        Assert.Equal(_upstream.Tarball("case-pkg", "1.0.0-beta").Bytes, await lower.Content.ReadAsByteArrayAsync(Cancel));
    }

    [Fact]
    public async Task ALegacyUpperCaseNameIsServedFromTheUpstreamAndItsTarballIsCached()
    {
        _upstream.Add("JSONStream", "1.3.5");
        await CreateRemoteAsync();

        var (status, packument) = await PackumentAsync("JSONStream");
        using var file = await TarballAsync("JSONStream", "1.3.5");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("JSONStream", (string?)packument!["name"]);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal(_upstream.Tarball("JSONStream", "1.3.5").Bytes, await file.Content.ReadAsByteArrayAsync(Cancel));
    }

    [Fact]
    public async Task ANewUpperCaseNameIsStillRefusedOnPublish()
    {
        await _app.CreateNpmRepositoryAsync("loc", cancellationToken: Cancel);

        var tarball = NpmTestPackages.Tarball("Upper", "1.0.0");
        using var response = await SendAsync(HttpMethod.Put, NpmTestPackages.Path("/r/loc/npm/", "Upper"), NpmTestPackages.PublishBody(tarball), token: Key);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- packuments ----

    [Fact]
    public async Task ThePackumentComesFromTheUpstreamWithEveryTarballPointingAtThisRepository()
    {
        _upstream.Add("left-pad", "1.0.0", "1.1.0");
        await CreateRemoteAsync();

        var (status, packument) = await PackumentAsync("left-pad");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("left-pad", (string?)packument!["name"]);
        Assert.Equal("1.1.0", (string?)packument["dist-tags"]!["latest"]);
        Assert.Equal("From upstream left-pad", (string?)packument["description"]);
        Assert.Equal("http://localhost/r/up/npm/left-pad/-/left-pad-1.0.0.tgz", (string?)packument["versions"]!["1.0.0"]!["dist"]!["tarball"]);
        Assert.Equal("http://localhost/r/up/npm/left-pad/-/left-pad-1.1.0.tgz", (string?)packument["versions"]!["1.1.0"]!["dist"]!["tarball"]);
        Assert.Equal(_upstream.Tarball("left-pad", "1.0.0").Integrity, (string?)packument["versions"]!["1.0.0"]!["dist"]!["integrity"]);
        Assert.Equal("2019-02-01T10:00:00.000Z", (string?)packument["time"]!["1.0.0"]);
        Assert.Equal(0, _upstream.Count("/-/"));
    }

    [Fact]
    public async Task AnInstallOnlyRequestAsksTheUpstreamForTheInstallDocument()
    {
        _upstream.Add("left-pad", "1.0.0");
        await CreateRemoteAsync();

        using var response = await SendAsync(HttpMethod.Get, Registry + "left-pad", accept: "application/vnd.npm.install-v1+json");
        var packument = JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancel))!;

        Assert.Equal("application/vnd.npm.install-v1+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("application/vnd.npm.install-v1+json", _upstream.Requests.Single().Accept);
        Assert.Null(packument["readme"]);
        Assert.Equal("http://localhost/r/up/npm/left-pad/-/left-pad-1.0.0.tgz", (string?)packument["versions"]!["1.0.0"]!["dist"]!["tarball"]);
    }

    [Fact]
    public async Task AScopedPackageIsAskedOfTheUpstreamWithAnEncodedSlashAndComesBackScoped()
    {
        _upstream.Add("@acme/tool", "2.0.0");
        await CreateRemoteAsync();

        var (status, packument) = await PackumentAsync("@acme/tool");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("@acme/tool", (string?)packument!["name"]);
        Assert.Contains("%2f", _upstream.Requests.Single().RawPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("http://localhost/r/up/npm/@acme/tool/-/tool-2.0.0.tgz", (string?)packument["versions"]!["2.0.0"]!["dist"]!["tarball"]);

        using var file = await SendAsync(HttpMethod.Get, Registry + "@acme/tool/-/tool-2.0.0.tgz");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal(_upstream.Tarball("@acme/tool", "2.0.0").Bytes, await file.Content.ReadAsByteArrayAsync(Cancel));
    }

    [Fact]
    public async Task AnAnswerInsideTheMetadataPeriodIsNotAskedForAgainAndAnExpiredOneIs()
    {
        _upstream.Add("left-pad", "1.0.0");
        await CreateRemoteAsync();

        await PackumentAsync("left-pad");
        await PackumentAsync("left-pad");
        Assert.Single(_upstream.Requests);

        _time.Advance(TimeSpan.FromSeconds(601));
        _upstream.Add("left-pad", "1.2.0");
        var (_, packument) = await PackumentAsync("left-pad");

        Assert.Equal(2, _upstream.Requests.Length);
        Assert.NotNull(packument!["versions"]!["1.2.0"]);
    }

    [Fact]
    public async Task AnExpiredAnswerIsServedWhileTheUpstreamIsDown()
    {
        _upstream.Add("left-pad", "1.0.0");
        await CreateRemoteAsync();
        await PackumentAsync("left-pad");

        _time.Advance(TimeSpan.FromSeconds(601));
        _wire.Down = true;
        var (status, packument) = await PackumentAsync("left-pad");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotNull(packument!["versions"]!["1.0.0"]);
        using var scope = _app.Services.CreateScope();
        var cache = await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().GetCacheAsync(Remote, Cancel);
        Assert.Equal(1, cache.Status.StaleServed);
        Assert.NotNull(cache.Status.FailingSinceUtc);
    }

    [Fact]
    public async Task AnUnreachableUpstreamWithNothingStoredOrRememberedIsABadGatewayNotAMissingPackage()
    {
        _upstream.Add("left-pad", "1.0.0");
        await CreateRemoteAsync();
        _wire.Down = true;

        using var response = await SendAsync(HttpMethod.Get, Registry + "left-pad");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var error = (string?)JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancel))!["error"];
        Assert.False(string.IsNullOrEmpty(error));
        Assert.DoesNotContain("registry.test", error);
    }

    [Fact]
    public async Task AMissingPackageIsRememberedAsMissingForTheNegativePeriodOnly()
    {
        await CreateRemoteAsync();

        var (first, _) = await PackumentAsync("nothing-here");
        var (second, _) = await PackumentAsync("nothing-here");
        Assert.Equal(HttpStatusCode.NotFound, first);
        Assert.Equal(HttpStatusCode.NotFound, second);
        Assert.Single(_upstream.Requests);

        _time.Advance(TimeSpan.FromSeconds(61));
        _upstream.Add("nothing-here", "1.0.0");
        var (third, packument) = await PackumentAsync("nothing-here");

        Assert.Equal(HttpStatusCode.OK, third);
        Assert.NotNull(packument);
    }

    [Fact]
    public async Task AFailureToReachTheUpstreamIsNeverRememberedAsNotFound()
    {
        _upstream.Add("left-pad", "1.0.0");
        await CreateRemoteAsync();

        _wire.Down = true;
        using var down = await SendAsync(HttpMethod.Get, Registry + "left-pad");
        Assert.Equal(HttpStatusCode.BadGateway, down.StatusCode);

        _wire.Down = false;
        var (status, _) = await PackumentAsync("left-pad");

        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Fact]
    public async Task AnUpstreamThatRefusesTheCredentialsIsABadGatewayAndNeverLeaksThem()
    {
        _upstream.Add("left-pad", "1.0.0");
        _upstream.RequiredAuthorization = "Bearer right";
        await CreateRemoteAsync(s => s.Password = "wrong");

        using var response = await SendAsync(HttpMethod.Get, Registry + "left-pad");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Cancel);
        Assert.DoesNotContain("wrong", body);
        Assert.DoesNotContain("Bearer", body);
    }

    // ---- credentials ----

    [Fact]
    public async Task ATokenWithoutAUserNameIsSentAsABearerAndAUserNameMakesItBasic()
    {
        _upstream.Add("left-pad", "1.0.0");
        await CreateRemoteAsync(s => s.Password = "tok");

        await PackumentAsync("left-pad");
        Assert.Equal("Bearer tok", _upstream.Requests.Single().Authorization);

        await UpdateAsync(s =>
        {
            s.Username = "bob";
            s.Password = "pw";
        });
        _time.Advance(TimeSpan.FromSeconds(601));
        await PackumentAsync("left-pad");

        Assert.Equal("Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("bob:pw")), _upstream.Requests.Last().Authorization);
    }

    // ---- tarballs ----

    [Fact]
    public async Task ATarballIsFetchedStoredAndThenServedFromTheRepositoryWhileTheUpstreamIsDown()
    {
        var tarballs = _upstream.Add("left-pad", "1.0.0", "1.1.0");
        await CreateRemoteAsync();

        using var first = await TarballAsync("left-pad", "1.0.0");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(tarballs[0].Bytes, await first.Content.ReadAsByteArrayAsync(Cancel));
        Assert.Equal("\"" + tarballs[0].Integrity + "\"", first.Headers.ETag?.Tag);

        var stored = await StoredAsync("left-pad");
        Assert.Equal("1.0.0", Assert.Single(stored!.Versions).Version);
        Assert.Equal(tarballs[0].Integrity, stored.Versions[0].Integrity);
        Assert.Equal(1, await StoredPackagesAsync());

        _wire.Down = true;
        using var again = await TarballAsync("left-pad", "1.0.0");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(tarballs[0].Bytes, await again.Content.ReadAsByteArrayAsync(Cancel));
        Assert.Equal(1, _upstream.TarballDownloads);

        // The version never asked for is not there.
        using var other = await TarballAsync("left-pad", "1.1.0");
        Assert.Equal(HttpStatusCode.BadGateway, other.StatusCode);
    }

    [Fact]
    public async Task WhatWasStoredStillAnswersAPackumentWhenTheUpstreamAndItsMemoryAreGone()
    {
        _upstream.Add("left-pad", "1.0.0", "1.1.0");
        await CreateRemoteAsync();
        await PackumentAsync("left-pad");
        using (await TarballAsync("left-pad", "1.0.0")) { }

        // A restart empties what this node remembers; the upstream is down.
        var key = (await StoredAsync("left-pad"))!.Versions.Count;
        using (var scope = _app.Services.CreateScope())
        {
            var info = (await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync(Remote, Cancel))!;
            _app.Services.GetRequiredService<RemoteRepositoryState>().Clear(info.Key);
        }

        Assert.Equal(1, key);
        _wire.Down = true;
        var (status, packument) = await PackumentAsync("left-pad");

        Assert.Equal(HttpStatusCode.OK, status);
        var versions = ((JsonObject)packument!["versions"]!).Select(v => v.Key).ToList();
        Assert.Equal(new[] { "1.0.0" }, versions);
        Assert.Equal("1.0.0", (string?)packument["dist-tags"]!["latest"]);
        Assert.Equal("http://localhost/r/up/npm/left-pad/-/left-pad-1.0.0.tgz", (string?)packument["versions"]!["1.0.0"]!["dist"]!["tarball"]);
    }

    [Fact]
    public async Task APackageTheUpstreamNoLongerHasIsStillServedFromWhatWasStored()
    {
        _upstream.Add("left-pad", "1.0.0");
        await CreateRemoteAsync();
        using (await TarballAsync("left-pad", "1.0.0")) { }

        // The upstream answers 404 for it once the remembered answer has expired.
        _time.Advance(TimeSpan.FromSeconds(601));
        _upstream.Remove("left-pad");
        var (status, packument) = await PackumentAsync("left-pad");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(new[] { "1.0.0" }, ((JsonObject)packument!["versions"]!).Select(v => v.Key));
    }

    [Fact]
    public async Task ConcurrentRequestsForOneTarballDownloadItOnce()
    {
        _upstream.Add("left-pad", "1.0.0");
        await CreateRemoteAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var response = await TarballAsync("left-pad", "1.0.0");

            return (response.StatusCode, await response.Content.ReadAsByteArrayAsync(Cancel));
        }));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.All(responses, r => Assert.Equal(_upstream.Tarball("left-pad", "1.0.0").Bytes, r.Item2));
        Assert.Equal(1, _upstream.TarballDownloads);
        Assert.Equal(1, await StoredPackagesAsync());
    }

    [Fact]
    public async Task ATarballThatDoesNotMatchTheIntegrityOfItsPackumentIsNeitherServedNorStored()
    {
        _upstream.Add("left-pad", "1.0.0");
        _upstream.Corrupt.Add("left-pad@1.0.0");
        await CreateRemoteAsync();

        using var response = await TarballAsync("left-pad", "1.0.0");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(0, await StoredPackagesAsync());
        Assert.Null(await StoredAsync("left-pad"));

        using var scope = _app.Services.CreateScope();
        var status = (await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().GetCacheAsync(Remote, Cancel)).Status;
        Assert.NotNull(status.FailingSinceUtc);
    }

    [Fact]
    public async Task ATarballTheUpstreamListsButDoesNotHaveIsNotFoundAndRememberedForTheNegativePeriod()
    {
        _upstream.Add("left-pad", "1.0.0");
        _upstream.MissingFiles.Add("left-pad@1.0.0");
        await CreateRemoteAsync();

        using (var first = await TarballAsync("left-pad", "1.0.0")) Assert.Equal(HttpStatusCode.NotFound, first.StatusCode);
        var downloads = _upstream.Count("/-/left-pad");
        using (var second = await TarballAsync("left-pad", "1.0.0")) Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);

        Assert.Equal(downloads, _upstream.Count("/-/left-pad"));
    }

    [Fact]
    public async Task ATarballUrlOnAnotherHostIsNotFollowed()
    {
        _upstream.Add("left-pad", "1.0.0");
        _upstream.TarballOrigin = "http://elsewhere.test/";
        await CreateRemoteAsync(s => s.Password = "tok");

        using var response = await TarballAsync("left-pad", "1.0.0");

        // The conventional path on the upstream's own origin answers; nothing goes to the host the packument named.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.All(_wire.Requests, r => Assert.Equal("registry.test", r.Uri.Host));
    }

    [Fact]
    public async Task APassThroughRepositoryStreamsTheTarballAndStoresNothing()
    {
        var tarballs = _upstream.Add("left-pad", "1.0.0");
        await CreateRemoteAsync(s => s.StoreLocally = false);

        using var response = await TarballAsync("left-pad", "1.0.0");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(tarballs[0].Bytes, await response.Content.ReadAsByteArrayAsync(Cancel));
        Assert.Equal(0, await StoredPackagesAsync());
        Assert.Null(await StoredAsync("left-pad"));

        using var again = await TarballAsync("left-pad", "1.0.0");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(2, _upstream.TarballDownloads);
    }

    [Fact]
    public async Task OfflineModeNeverAsksTheUpstreamAndServesWhatIsStored()
    {
        var tarballs = _upstream.Add("left-pad", "1.0.0");
        _upstream.Add("other", "1.0.0");
        await CreateRemoteAsync();
        using (await TarballAsync("left-pad", "1.0.0")) { }

        await UpdateAsync(s => s.Offline = true);
        var before = _upstream.Requests.Length;

        var (known, packument) = await PackumentAsync("left-pad");
        var (unknown, _) = await PackumentAsync("other");
        using var file = await TarballAsync("left-pad", "1.0.0");
        using var missingFile = await TarballAsync("other", "1.0.0");

        Assert.Equal(HttpStatusCode.OK, known);
        Assert.Equal(new[] { "1.0.0" }, ((JsonObject)packument!["versions"]!).Select(v => v.Key));
        Assert.Equal(HttpStatusCode.NotFound, unknown);
        Assert.Equal(tarballs[0].Bytes, await file.Content.ReadAsByteArrayAsync(Cancel));
        Assert.Equal(HttpStatusCode.NotFound, missingFile.StatusCode);
        Assert.Equal(before, _upstream.Requests.Length);
    }

    // ---- writes, search ----

    [Fact]
    public async Task EveryWriteToARemoteRepositoryIsRefused()
    {
        _upstream.Add("left-pad", "1.0.0");
        await CreateRemoteAsync();
        using (await TarballAsync("left-pad", "1.0.0")) { }
        var tarball = NpmTestPackages.Tarball("fresh", "1.0.0");

        var responses = new[]
        {
            await SendAsync(HttpMethod.Put, Registry + "fresh", NpmTestPackages.PublishBody(tarball), token: Key),
            await SendAsync(HttpMethod.Put, Registry + "-/package/left-pad/dist-tags/beta", "\"1.0.0\"", token: Key),
            await SendAsync(HttpMethod.Delete, Registry + "-/package/left-pad/dist-tags/latest", token: Key),
            await SendAsync(HttpMethod.Delete, Registry + "left-pad/-rev/1-x", token: Key),
            await SendAsync(HttpMethod.Delete, Registry + "left-pad/-/left-pad-1.0.0.tgz/-rev/1-x", token: Key),
        };

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
        Assert.Null(await StoredAsync("fresh"));
        Assert.Equal("1.0.0", Assert.Single((await StoredAsync("left-pad"))!.Versions).Version);

        // Without credentials the answer is the usual challenge.
        using var anonymous = await SendAsync(HttpMethod.Put, Registry + "fresh", NpmTestPackages.PublishBody(tarball));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task SearchListsTheStoredPackagesOnly()
    {
        _upstream.Add("left-pad", "1.0.0");
        _upstream.Add("right-pad", "1.0.0");
        await CreateRemoteAsync();
        using (await TarballAsync("left-pad", "1.0.0")) { }

        using var response = await SendAsync(HttpMethod.Get, Registry + "-/v1/search?text=pad");
        var objects = (JsonArray)JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancel))!["objects"]!;

        Assert.Equal(new[] { "left-pad" }, objects.Select(o => (string?)o!["package"]!["name"]));
    }

    // ---- administration ----

    [Fact]
    public async Task AnAdministratorSeesTheStoredPackagesAndPurgesThem()
    {
        _upstream.Add("left-pad", "1.0.0", "1.1.0");
        _upstream.Add("right-pad", "2.0.0");
        await CreateRemoteAsync();
        foreach (var (name, version) in new[] { ("left-pad", "1.0.0"), ("left-pad", "1.1.0"), ("right-pad", "2.0.0") })
        {
            using var _ = await TarballAsync(name, version);
        }

        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();
        var cache = await admin.GetCacheAsync(Remote, Cancel);
        Assert.Equal(3, cache.PackageCount);
        Assert.Equal(2, cache.PackageIdCount);

        // One version of one package.
        var one = await admin.PurgePackageAsync(Remote, "left-pad", NuGet.Versioning.NuGetVersion.Parse("1.0.0"), Cancel);
        Assert.Equal(1, one.Packages);
        var left = await StoredAsync("left-pad");
        Assert.Equal("1.1.0", Assert.Single(left!.Versions).Version);
        Assert.Equal("1.1.0", left.DistTags["latest"]);

        // The rest.
        var all = await admin.PurgeAsync(Remote, Cancel);
        Assert.Equal(2, all.Packages);
        Assert.Equal(0, await StoredPackagesAsync());
        Assert.Null(await StoredAsync("left-pad"));
        Assert.Null(await StoredAsync("right-pad"));

        // The next request fetches again.
        using var again = await TarballAsync("left-pad", "1.0.0");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(2, _upstream.Requests.Count(r => r.Path.EndsWith("left-pad-1.0.0.tgz")));
    }

    [Fact]
    public async Task APurgeDropsTheRememberedAnswersOfThePackage()
    {
        _upstream.Add("left-pad", "1.0.0");
        await CreateRemoteAsync();
        await PackumentAsync("left-pad");
        await PackumentAsync("left-pad");
        Assert.Single(_upstream.Requests);

        using var scope = _app.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().PurgePackageAsync(Remote, "left-pad", null, Cancel);
        Assert.Equal(1, result.CachedAnswers);

        await PackumentAsync("left-pad");
        Assert.Equal(2, _upstream.Requests.Length);
    }

    [Fact]
    public async Task APurgeOfAScopedPackageDropsItsAnswers()
    {
        _upstream.Add("@acme/tool", "1.0.0");
        await CreateRemoteAsync();
        await PackumentAsync("@acme/tool");
        using (await SendAsync(HttpMethod.Get, Registry + "@acme/tool/-/tool-1.0.0.tgz")) { }

        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();
        var result = await admin.PurgePackageAsync(Remote, "@acme/tool", null, Cancel);

        Assert.Equal(1, result.Packages);
        Assert.True(result.CachedAnswers >= 1);
        await Assert.ThrowsAsync<ArgumentException>(() => admin.PurgePackageAsync(Remote, "Not A Name", null, Cancel));
    }

    [Fact]
    public async Task ARemoteRepositoryHoldingPackagesCannotBeDeletedUntilItIsPurged()
    {
        _upstream.Add("left-pad", "1.0.0");
        await CreateRemoteAsync();
        using (await TarballAsync("left-pad", "1.0.0")) { }

        using (var scope = _app.Services.CreateScope())
        {
            await Assert.ThrowsAsync<RepositoryNotEmptyException>(() => scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().DeleteAsync(Remote, Cancel));
            await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().PurgeAsync(Remote, Cancel);
        }

        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().DeleteAsync(Remote, Cancel);
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync(Remote, Cancel));
        }
    }

    [Fact]
    public async Task TestConnectionAsksThePingAndFallsBackToTheRootWhereThereIsNone()
    {
        await CreateRemoteAsync();
        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();

        var ok = await admin.TestConnectionAsync(Remote, Settings(), Cancel);
        Assert.True(ok.Success, ok.Message);
        Assert.Contains("npm registry", ok.Message);
        Assert.Equal("/-/ping", _upstream.Requests.Single().Path);

        _upstream.PingMissing = true;
        var fallback = await admin.TestConnectionAsync(Remote, Settings(), Cancel);
        Assert.True(fallback.Success, fallback.Message);
        Assert.Equal("/", _upstream.Requests.Last().Path);
    }

    [Fact]
    public async Task TestConnectionReportsRefusedCredentialsAndAnUnreachableUpstream()
    {
        await CreateRemoteAsync();
        _upstream.RequiredAuthorization = "Bearer right";
        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();

        var refused = await admin.TestConnectionAsync(Remote, Settings(s => s.Password = "wrong"), Cancel);
        Assert.False(refused.Success);
        Assert.Equal(401, refused.StatusCode);
        Assert.DoesNotContain("wrong", refused.Message);

        var accepted = await admin.TestConnectionAsync(Remote, Settings(s => s.Password = "right"), Cancel);
        Assert.True(accepted.Success, accepted.Message);

        _wire.Down = true;
        var down = await admin.TestConnectionAsync(Remote, Settings(s => s.Password = "right"), Cancel);
        Assert.False(down.Success);
    }

    [Fact]
    public async Task APrivateUpstreamAddressIsRefusedWhenTheServerBlocksThem()
    {
        using var guarded = new BaGetApplication(
            new XunitOutput(),
            configOverrides: new Dictionary<string, string> { { "Remote:BlockPrivateAddresses", "true" } });
        using var scope = guarded.Services.CreateScope();

        var e = await Assert.ThrowsAsync<ArgumentException>(() => scope.ServiceProvider.GetRequiredService<IRepositoryService>()
            .CreateAsync("up", RepositoryFormat.Npm, RepositoryKind.Remote, Settings(s => s.Url = "http://localhost:4873/"), anonymousRead: true, Cancel));

        Assert.Contains("private", e.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class XunitOutput : ITestOutputHelper
    {
        public string Output => string.Empty;

        public void Write(string message)
        {
        }

        public void Write(string format, params object[] args)
        {
        }

        public void WriteLine(string message)
        {
        }

        public void WriteLine(string format, params object[] args)
        {
        }
    }
}
