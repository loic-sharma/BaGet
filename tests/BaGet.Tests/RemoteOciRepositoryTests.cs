using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>A clock the test moves, for the code that asks <see cref="SystemTime"/> (when a blob or a session was made).</summary>
internal sealed class ManualSystemTime : SystemTime
{
    private DateTime _now = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public override DateTime UtcNow => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>
/// Remote Docker repositories (P5): a repository that caches an upstream registry such as Docker Hub. The upstream is a
/// <see cref="FakeOciUpstream"/> in memory (with the token flow, a CDN host layers are redirected to, private images and the other
/// things a real registry does to a proxy), reached through <see cref="ForwardingRemoteHttpClientFactory"/> (which counts what it
/// receives and can pretend to be down); time is a <see cref="ManualTimeProvider"/>. The real Docker client is in
/// <c>BaGet.Clients.Tests</c>.
/// </summary>
public class RemoteOciRepositoryTests : IDisposable
{
    private const string Key = "secret-key";
    private const string Remote = "hub";
    private const string UpstreamUrl = "https://" + FakeOciUpstream.RegistryHost;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly FakeOciUpstream _upstream = new();
    private readonly ForwardingRemoteHttpClientFactory _wire;
    private readonly ManualTimeProvider _time = new();
    private readonly ManualSystemTime _clock = new();
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public RemoteOciRepositoryTests(ITestOutputHelper output)
    {
        _wire = new ForwardingRemoteHttpClientFactory(new HttpClient(_upstream));
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string> { { "ApiKey", Key } },
            configureServices: services =>
            {
                services.AddSingleton<IRemoteHttpClientFactory>(_wire);
                services.AddSingleton(new RemoteRepositoryState(_time));
                services.AddSingleton<SystemTime>(_clock);
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
        // The create form posts the NuGet default protocol; the repository's format decides.
        var settings = new RemoteRepositorySettings { Url = UpstreamUrl, Protocol = RemoteProtocol.V3, MetadataTtlSeconds = 600, NegativeTtlSeconds = 60 };
        change?.Invoke(settings);

        return settings;
    }

    private async Task CreateRemoteAsync(Action<RemoteRepositorySettings>? change = null, string name = Remote, bool anonymousRead = true)
    {
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
            .CreateAsync(name, RepositoryFormat.Docker, RepositoryKind.Remote, Settings(change), anonymousRead, Cancel);
    }

    private async Task UpdateAsync(Action<RemoteRepositorySettings> change, string name = Remote)
    {
        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();
        var current = (await admin.FindAsync(name, Cancel))!.Settings;
        change(current);
        await admin.UpdateAsync(name, current, clearPassword: false, Cancel);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, byte[]? body = null, bool key = false, Action<HttpRequestMessage>? configure = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body != null) request.Content = new ByteArrayContent(body);
        if (key) request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("someone:" + Key)));
        configure?.Invoke(request);

        return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Cancel);
    }

    private Task<HttpResponseMessage> GetAsync(string path) => SendAsync(HttpMethod.Get, path);

    private static string ManifestPath(string image, string reference, string repo = Remote) => $"/v2/{repo}/{image}/manifests/{reference}";

    private static string BlobPath(string image, string digest, string repo = Remote) => $"/v2/{repo}/{image}/blobs/{digest}";

    private async Task<(int Manifests, int Images, long Bytes)> StoredAsync(string repository = Remote)
    {
        using var scope = _app.Services.CreateScope();
        var overview = await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().GetCacheAsync(repository, Cancel);

        return (overview.PackageCount, overview.PackageIdCount, overview.StoredBytes);
    }

    private async Task<bool> BlobStoredAsync(string digest, string repository = Remote)
    {
        using var scope = await _app.CreateRepositoryScopeAsync(repository, Cancel);

        return await scope.ServiceProvider.GetRequiredService<OciRegistryService>().FindRepositoryBlobAsync(digest, Cancel) != null;
    }

    private static string ErrorCode(string body)
    {
        using var json = JsonDocument.Parse(body);

        return json.RootElement.GetProperty("errors")[0].GetProperty("code").GetString()!;
    }

    // ---- creation ----

    [Fact]
    public async Task ARemoteDockerRepositoryIsCreatedWithTheDockerProtocolWhateverTheFormSent()
    {
        await CreateRemoteAsync();

        using var scope = _app.Services.CreateScope();
        var info = await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync(Remote, Cancel);
        Assert.Equal(RepositoryKind.Remote, info!.Kind);
        Assert.Equal(RepositoryFormat.Docker, info.Format);

        var detail = await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().FindAsync(Remote, Cancel);
        Assert.Equal(RemoteProtocol.Docker, detail!.Settings.Protocol);
        Assert.Equal(UpstreamUrl, detail.Settings.Url);
    }

    [Fact]
    public async Task ASavedRemoteDockerRepositoryKeepsTheDockerProtocol()
    {
        await CreateRemoteAsync();

        await UpdateAsync(s =>
        {
            s.Protocol = RemoteProtocol.V2;
            s.MetadataTtlSeconds = 5;
        });

        using var scope = _app.Services.CreateScope();
        var detail = await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().FindAsync(Remote, Cancel);
        Assert.Equal(RemoteProtocol.Docker, detail!.Settings.Protocol);
        Assert.Equal(5, detail.Settings.MetadataTtlSeconds);
    }

    [Fact]
    public async Task ARegistryUrlWithAQueryStringIsRefused()
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => CreateRemoteAsync(s => s.Url = UpstreamUrl + "?x=1"));

        Assert.Contains("must not have a query string", error.Message);
    }

    // ---- manifests ----

    [Fact]
    public async Task AManifestIsFetchedThroughTheTokenFlowServedByteForByteAndKept()
    {
        var digest = _upstream.AddImage("team/app", "1.0", "a");
        await CreateRemoteAsync();

        using var response = await GetAsync(ManifestPath("team/app", "1.0"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(digest, response.Headers.GetValues("Docker-Content-Digest").Single());
        Assert.Equal(FakeOciUpstream.ManifestType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(digest, FakeOciUpstream.Digest(await response.Content.ReadAsByteArrayAsync(Cancel)));

        // The first request was challenged; the second went to the token service named in the challenge and carried its token.
        var requests = _upstream.Requests;
        Assert.Equal(1, _upstream.TokenRequests);
        Assert.Contains(requests, r => r.Host == FakeOciUpstream.RegistryHost && r.Authorization == null);
        Assert.Contains(requests, r => r.Host == FakeOciUpstream.RegistryHost && r.Authorization?.StartsWith("Bearer tok", StringComparison.Ordinal) == true);
        Assert.Equal((1, 1, 0L), await StoredAsync());
    }

    [Fact]
    public async Task AKeptManifestIsServedWithoutAskingTheUpstreamAgainWithinTheMetadataPeriod()
    {
        _upstream.AddImage("team/app", "1.0", "a");
        await CreateRemoteAsync();
        (await GetAsync(ManifestPath("team/app", "1.0"))).Dispose();
        var before = _upstream.Requests.Length;

        using var again = await GetAsync(ManifestPath("team/app", "1.0"));

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(before, _upstream.Requests.Length);
    }

    [Fact]
    public async Task AnExpiredTagIsCheckedWithAHeadAndTheManifestIsNotDownloadedAgainWhenItIsUnchanged()
    {
        _upstream.AddImage("team/app", "1.0", "a");
        await CreateRemoteAsync();
        (await GetAsync(ManifestPath("team/app", "1.0"))).Dispose();
        Assert.Equal(1, _upstream.Count("GET", "/manifests/1.0"));

        _time.Advance(TimeSpan.FromSeconds(601));
        using var again = await GetAsync(ManifestPath("team/app", "1.0"));

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(1, _upstream.Count("HEAD", "/manifests/1.0"));
        Assert.Equal(1, _upstream.Count("GET", "/manifests/1.0"));
    }

    [Fact]
    public async Task ATagThatMovedUpstreamIsFetchedAgainAndMovesHere()
    {
        var first = _upstream.AddImage("team/app", "latest", "a");
        await CreateRemoteAsync();
        using (var before = await GetAsync(ManifestPath("team/app", "latest")))
        {
            Assert.Equal(first, before.Headers.GetValues("Docker-Content-Digest").Single());
        }

        var second = _upstream.AddImage("team/app", "latest", "b");
        _time.Advance(TimeSpan.FromSeconds(601));
        using var after = await GetAsync(ManifestPath("team/app", "latest"));

        Assert.Equal(second, after.Headers.GetValues("Docker-Content-Digest").Single());
        Assert.Equal(2, _upstream.Count("GET", "/manifests/latest"));

        // Both manifests are kept, the tag points at the new one; the old one is still there by digest.
        Assert.Equal((2, 1, 0L), await StoredAsync());
        using var old = await GetAsync(ManifestPath("team/app", first));
        Assert.Equal(HttpStatusCode.OK, old.StatusCode);
    }

    [Fact]
    public async Task AManifestByDigestThatIsKeptNeverAsksTheUpstream()
    {
        var digest = _upstream.AddImage("team/app", "1.0", "a");
        await CreateRemoteAsync();
        (await GetAsync(ManifestPath("team/app", "1.0"))).Dispose();
        _time.Advance(TimeSpan.FromDays(30));
        var before = _upstream.Requests.Length;

        using var response = await GetAsync(ManifestPath("team/app", digest));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, _upstream.Requests.Length);
    }

    [Fact]
    public async Task AManifestByDigestThatIsNotKeptYetIsFetchedAndKeptWithoutATag()
    {
        var digest = _upstream.AddImage("team/app", "1.0", "a");
        await CreateRemoteAsync();

        using var response = await GetAsync(ManifestPath("team/app", digest));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = await _app.CreateRepositoryScopeAsync(Remote, Cancel);
        var service = scope.ServiceProvider.GetRequiredService<OciRegistryService>();
        Assert.NotNull(await service.FindManifestAsync("team/app", digest, Cancel));
        Assert.Null(await service.FindTagDigestAsync("team/app", "1.0", Cancel));
    }

    [Fact]
    public async Task AManifestThatDoesNotHaveTheDigestAskedForIsNeitherServedNorKept()
    {
        var digest = _upstream.AddImage("team/app", "1.0", "a");
        _upstream.LyingDigest = true;
        await CreateRemoteAsync();

        using var response = await GetAsync(ManifestPath("team/app", "1.0"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("UNAVAILABLE", ErrorCode(await response.Content.ReadAsStringAsync(Cancel)));
        Assert.Equal((0, 0, 0L), await StoredAsync());
        Assert.NotNull(digest);
    }

    [Fact]
    public async Task AnImageTheUpstreamDoesNotHaveIs404AndThatIsRememberedForTheNegativePeriod()
    {
        await CreateRemoteAsync();

        using (var first = await GetAsync(ManifestPath("team/app", "9.9"))) Assert.Equal(HttpStatusCode.NotFound, first.StatusCode);
        using (var second = await GetAsync(ManifestPath("team/app", "9.9"))) Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal(1, _upstream.Count("GET", "/manifests/9.9"));

        _time.Advance(TimeSpan.FromSeconds(61));
        _upstream.AddImage("team/app", "9.9", "late");
        using var third = await GetAsync(ManifestPath("team/app", "9.9"));

        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
    }

    [Fact]
    public async Task AnUpstreamThatCannotBeReachedIs502AndIsNeverRememberedAsNotFound()
    {
        _upstream.AddImage("team/app", "1.0", "a");
        await CreateRemoteAsync();
        _wire.Down = true;

        using (var down = await GetAsync(ManifestPath("team/app", "1.0")))
        {
            Assert.Equal(HttpStatusCode.BadGateway, down.StatusCode);
            Assert.Equal("UNAVAILABLE", ErrorCode(await down.Content.ReadAsStringAsync(Cancel)));
        }

        _wire.Down = false;
        using var up = await GetAsync(ManifestPath("team/app", "1.0"));
        Assert.Equal(HttpStatusCode.OK, up.StatusCode);
    }

    [Fact]
    public async Task AKeptManifestIsStillServedWhileTheUpstreamIsDown()
    {
        _upstream.AddImage("team/app", "1.0", "a");
        await CreateRemoteAsync();
        (await GetAsync(ManifestPath("team/app", "1.0"))).Dispose();
        _time.Advance(TimeSpan.FromDays(1));
        _wire.Down = true;

        using var response = await GetAsync(ManifestPath("team/app", "1.0"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OfflineModeNeverContactsTheUpstreamAndServesWhatIsKept()
    {
        _upstream.AddImage("team/app", "1.0", "a");
        _upstream.AddImage("team/other", "1.0", "b");
        await CreateRemoteAsync();
        (await GetAsync(ManifestPath("team/app", "1.0"))).Dispose();
        await UpdateAsync(s => s.Offline = true);
        var before = _upstream.Requests.Length;

        using var kept = await GetAsync(ManifestPath("team/app", "1.0"));
        using var missing = await GetAsync(ManifestPath("team/other", "1.0"));

        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(before, _upstream.Requests.Length);
    }

    [Fact]
    public async Task WithoutStoreLocallyNothingIsKeptAndEveryPeriodAsksAgain()
    {
        _upstream.AddImage("team/app", "1.0", "a");
        await CreateRemoteAsync(s => s.StoreLocally = false);

        using var response = await GetAsync(ManifestPath("team/app", "1.0"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((0, 0, 0L), await StoredAsync());

        _time.Advance(TimeSpan.FromSeconds(601));
        (await GetAsync(ManifestPath("team/app", "1.0"))).Dispose();
        Assert.Equal(1, _upstream.Count("HEAD", "/manifests/1.0"));
    }

    [Fact]
    public async Task AMultiPlatformIndexAndTheChildThePlatformPicksAreFetchedAndKept()
    {
        var amd64 = _upstream.AddImage("team/multi", "amd64", "a");
        var arm64 = _upstream.AddImage("team/multi", "arm64", "b");
        var index = _upstream.AddIndex("team/multi", "latest", (amd64, "linux", "amd64"), (arm64, "linux", "arm64"));
        await CreateRemoteAsync();

        // A client asks for the index by tag, then for the one child its platform needs, by digest, then for that child's layers.
        using var list = await GetAsync(ManifestPath("team/multi", "latest"));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(index, list.Headers.GetValues("Docker-Content-Digest").Single());
        Assert.Equal("application/vnd.oci.image.index.v1+json", list.Content.Headers.ContentType?.MediaType);

        using var child = await GetAsync(ManifestPath("team/multi", arm64));
        Assert.Equal(HttpStatusCode.OK, child.StatusCode);
        var layer = _upstream.LayersOf("team/multi", "arm64")[0];
        using var blob = await GetAsync(BlobPath("team/multi", layer));
        Assert.Equal(_upstream.Blob(layer), await blob.Content.ReadAsByteArrayAsync(Cancel));

        // The index and the child are kept; the child nobody asked for is not fetched.
        Assert.Equal((2, 1, (await StoredAsync()).Bytes), await StoredAsync());
        Assert.True((await StoredAsync()).Bytes > 0);
        Assert.Equal(0, _upstream.Count("GET", "manifests/" + amd64));

        using var scope = await _app.CreateRepositoryScopeAsync(Remote, Cancel);
        var detail = await scope.ServiceProvider.GetRequiredService<IOciRegistry>().GetManifestDetailAsync("team/multi", index, Cancel);
        Assert.Equal(new[] { "linux/amd64", "linux/arm64" }, detail!.Platforms.Select(p => p.Platform));
        Assert.Equal(new[] { false, true }, detail.Platforms.Select(p => p.Present));
    }

    // ---- blobs ----

    [Fact]
    public async Task ALayerIsStreamedFromTheUpstreamVerifiedAndKeptForTheNextRequest()
    {
        _upstream.AddImage("team/app", "1.0", "a", layerSize: 5000);
        var layer = _upstream.LayersOf("team/app", "1.0")[0];
        await CreateRemoteAsync();
        (await GetAsync(ManifestPath("team/app", "1.0"))).Dispose();

        using (var first = await GetAsync(BlobPath("team/app", layer)))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(layer, first.Headers.GetValues("Docker-Content-Digest").Single());
            Assert.Equal(_upstream.Blob(layer), await first.Content.ReadAsByteArrayAsync(Cancel));
        }

        Assert.True(await BlobStoredAsync(layer));

        var gets = _upstream.Count("GET", "/blobs/" + layer);
        using (var second = await GetAsync(BlobPath("team/app", layer)))
        {
            Assert.Equal(_upstream.Blob(layer), await second.Content.ReadAsByteArrayAsync(Cancel));
        }

        Assert.Equal(gets, _upstream.Count("GET", "/blobs/" + layer));
        Assert.Equal(1, gets);
    }

    [Fact]
    public async Task AKeptLayerCountsAsUsedByTheManifestThatNamesIt()
    {
        _upstream.AddImage("team/app", "1.0", "a");
        var layer = _upstream.LayersOf("team/app", "1.0")[0];
        await CreateRemoteAsync();
        (await GetAsync(ManifestPath("team/app", "1.0"))).Dispose();
        (await GetAsync(BlobPath("team/app", layer))).Dispose();
        await (await GetAsync(BlobPath("team/app", layer))).Content.ReadAsByteArrayAsync(Cancel);

        using var scope = await _app.CreateRepositoryScopeAsync(Remote, Cancel);
        var info = await scope.ServiceProvider.GetRequiredService<OciRegistryService>().GetImageAsync("team/app", Cancel);

        // The layer is referenced by the cached manifest, so nothing is "unused" and the garbage collector leaves it alone.
        Assert.Equal(0, info!.UnreferencedBlobs);
    }

    [Fact]
    public async Task ALayerRedirectedToACdnIsFetchedFromThereWithoutTheRegistrysCredential()
    {
        _upstream.AddImage("team/app", "1.0", "a");
        _upstream.RedirectBlobs = true;
        var layer = _upstream.LayersOf("team/app", "1.0")[0];
        await CreateRemoteAsync();
        (await GetAsync(ManifestPath("team/app", "1.0"))).Dispose();

        using var response = await GetAsync(BlobPath("team/app", layer));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(_upstream.Blob(layer), await response.Content.ReadAsByteArrayAsync(Cancel));
        Assert.NotEmpty(_upstream.CdnRequests);
        Assert.All(_upstream.CdnRequests, r => Assert.Null(r.Authorization));
    }

    [Fact]
    public async Task AChallengeFromTheHostARedirectLedToIsNotAnsweredWithTheCredential()
    {
        _upstream.AddImage("team/app", "1.0", "a");
        _upstream.RedirectBlobs = true;
        _upstream.CdnChallenges = true;
        var layer = _upstream.LayersOf("team/app", "1.0")[0];
        await CreateRemoteAsync();
        (await GetAsync(ManifestPath("team/app", "1.0"))).Dispose();

        using var response = await GetAsync(BlobPath("team/app", layer));

        // The 401 came from the CDN, not the registry: its realm is never asked for a token (and so never given the credential).
        Assert.NotEmpty(_upstream.CdnRequests);
        Assert.Empty(_upstream.OtherRealmRequests);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ALayerThatDoesNotHaveItsDigestIsNeverKept()
    {
        _upstream.AddImage("team/app", "1.0", "a");
        var layer = _upstream.LayersOf("team/app", "1.0")[0];
        _upstream.Corrupt.Add(layer);
        await CreateRemoteAsync();
        (await GetAsync(ManifestPath("team/app", "1.0"))).Dispose();

        // The bytes reach the client (it checks the digest itself, as Docker does) but the registry keeps nothing and ends the download in error.
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var response = await GetAsync(BlobPath("team/app", layer));
            await response.Content.ReadAsByteArrayAsync(Cancel);
        });

        Assert.False(await BlobStoredAsync(layer));
    }

    [Fact]
    public async Task ARangeOfALayerNotKeptYetPassesThroughAndKeepsNothing()
    {
        _upstream.AddImage("team/app", "1.0", "a", layerSize: 200);
        var layer = _upstream.LayersOf("team/app", "1.0")[0];
        await CreateRemoteAsync();
        (await GetAsync(ManifestPath("team/app", "1.0"))).Dispose();

        using var response = await SendAsync(HttpMethod.Get, BlobPath("team/app", layer), configure: r => r.Headers.Range = new RangeHeaderValue(50, 99));

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(_upstream.Blob(layer).Skip(50).Take(50).ToArray(), await response.Content.ReadAsByteArrayAsync(Cancel));
        Assert.Equal("bytes 50-99/200", response.Content.Headers.ContentRange!.ToString());
        Assert.False(await BlobStoredAsync(layer));
    }

    [Fact]
    public async Task ALayerAnotherImageOfTheRepositoryKeepsIsLinkedOnceTheUpstreamConfirmsItAndIsNotDownloadedAgain()
    {
        _upstream.AddImage("team/app", "1.0", "shared");
        var layer = _upstream.LayersOf("team/app", "1.0")[0];

        // A second image with the very same layer.
        var manifest = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(_upstream.Blob(_upstream.ConfigOf("team/app", "1.0"))).Length > 0
            ? $"{{\"schemaVersion\":2,\"mediaType\":\"{FakeOciUpstream.ManifestType}\",\"config\":{{\"mediaType\":\"application/vnd.oci.image.config.v1+json\",\"digest\":\"{_upstream.ConfigOf("team/app", "1.0")}\",\"size\":{_upstream.Blob(_upstream.ConfigOf("team/app", "1.0")).Length}}},\"layers\":[{{\"mediaType\":\"application/vnd.oci.image.layer.v1.tar+gzip\",\"digest\":\"{layer}\",\"size\":{_upstream.Blob(layer).Length}}}]}}"
            : string.Empty);
        _upstream.AddManifest("team/other", "2.0", FakeOciUpstream.ManifestType, manifest);
        await CreateRemoteAsync();

        (await GetAsync(ManifestPath("team/app", "1.0"))).Dispose();
        await (await GetAsync(BlobPath("team/app", layer))).Content.ReadAsByteArrayAsync(Cancel);
        Assert.Equal(1, _upstream.Count("GET", "/blobs/" + layer));

        (await GetAsync(ManifestPath("team/other", "2.0"))).Dispose();
        using var response = await GetAsync(BlobPath("team/other", layer));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(_upstream.Blob(layer), await response.Content.ReadAsByteArrayAsync(Cancel));

        // One download in all; the second image only asked whether the upstream has the layer for it.
        Assert.Equal(1, _upstream.Count("GET", "/blobs/" + layer));
        Assert.True(_upstream.Count("HEAD", "team/other/blobs/" + layer) >= 1);
    }

    [Fact]
    public async Task ALayerThatIsNotTheImagesIsNotServedFromAnotherImagesCopy()
    {
        _upstream.AddImage("team/app", "1.0", "a");
        var layer = _upstream.LayersOf("team/app", "1.0")[0];
        await CreateRemoteAsync();
        (await GetAsync(ManifestPath("team/app", "1.0"))).Dispose();
        await (await GetAsync(BlobPath("team/app", layer))).Content.ReadAsByteArrayAsync(Cancel);

        // The upstream has no image team/other: the kept layer is not handed out under it just because the digest is known.
        using var response = await GetAsync(BlobPath("team/other", layer));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ALayerWithoutStoreLocallyPassesThroughEveryTime()
    {
        _upstream.AddImage("team/app", "1.0", "a");
        var layer = _upstream.LayersOf("team/app", "1.0")[0];
        await CreateRemoteAsync(s => s.StoreLocally = false);

        for (var i = 0; i < 2; i++)
        {
            using var response = await GetAsync(BlobPath("team/app", layer));
            Assert.Equal(_upstream.Blob(layer), await response.Content.ReadAsByteArrayAsync(Cancel));
        }

        Assert.Equal(2, _upstream.Count("GET", "/blobs/" + layer));
        Assert.False(await BlobStoredAsync(layer));
    }

    [Fact]
    public async Task ConcurrentRequestsForOneLayerKeepItOnce()
    {
        _upstream.AddImage("team/app", "1.0", "a", layerSize: 20000);
        var layer = _upstream.LayersOf("team/app", "1.0")[0];
        await CreateRemoteAsync();
        (await GetAsync(ManifestPath("team/app", "1.0"))).Dispose();

        var bodies = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var response = await GetAsync(BlobPath("team/app", layer));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            return await response.Content.ReadAsByteArrayAsync(Cancel);
        }));

        Assert.All(bodies, b => Assert.Equal(_upstream.Blob(layer), b));
        Assert.True(await BlobStoredAsync(layer));
        Assert.Equal(1, (await StoredAsync()).Images);
    }

    [Fact]
    public async Task AHeadOfALayerNotKeptYetReportsTheUpstreamsSize()
    {
        _upstream.AddImage("team/app", "1.0", "a", layerSize: 321);
        var layer = _upstream.LayersOf("team/app", "1.0")[0];
        await CreateRemoteAsync();

        using var response = await SendAsync(HttpMethod.Head, BlobPath("team/app", layer));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(321, response.Content.Headers.ContentLength);
        Assert.False(await BlobStoredAsync(layer));
    }

    // ---- authentication ----

    [Fact]
    public async Task ATokenIsReusedWhileItLivesAndRenewedWhenItExpires()
    {
        _upstream.AddImage("team/app", "1.0", "a");
        _upstream.AddImage("team/app", "2.0", "b");
        _upstream.TokenLifetimeSeconds = 120;
        await CreateRemoteAsync(s => s.MetadataTtlSeconds = 0);

        (await GetAsync(ManifestPath("team/app", "1.0"))).Dispose();
        (await GetAsync(ManifestPath("team/app", "2.0"))).Dispose();
        Assert.Equal(1, _upstream.TokenRequests);

        _time.Advance(TimeSpan.FromSeconds(200));
        (await GetAsync(ManifestPath("team/app", "1.0"))).Dispose();

        Assert.Equal(2, _upstream.TokenRequests);
    }

    [Fact]
    public async Task ThePasswordGoesToTheTokenServiceAndToNothingElse()
    {
        _upstream.TokenCredentials = "puller:s3cret";
        _upstream.PrivateImages.Add("team/secret");
        _upstream.AddImage("team/secret", "1.0", "s");
        var layer = _upstream.LayersOf("team/secret", "1.0")[0];
        _upstream.RedirectBlobs = true;
        await CreateRemoteAsync(s =>
        {
            s.Username = "puller";
            s.Password = "s3cret";
        }, anonymousRead: true);

        using var manifest = await GetAsync(ManifestPath("team/secret", "1.0"));
        Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);
        using var blob = await GetAsync(BlobPath("team/secret", layer));
        Assert.Equal(HttpStatusCode.OK, blob.StatusCode);
        await blob.Content.ReadAsByteArrayAsync(Cancel);

        var basic = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("puller:s3cret"));
        Assert.All(_upstream.Requests.Where(r => r.Host == FakeOciUpstream.AuthHost), r => Assert.Equal(basic, r.Authorization));
        Assert.All(_upstream.Requests.Where(r => r.Host != FakeOciUpstream.AuthHost), r => Assert.NotEqual(basic, r.Authorization));
        Assert.DoesNotContain(_upstream.CdnRequests, r => r.Authorization != null);
    }

    [Fact]
    public async Task APrivateImageWithoutCredentialsIs404AndTheUpstreamIsNotMarkedFailing()
    {
        _upstream.TokenCredentials = "puller:s3cret";
        _upstream.PrivateImages.Add("team/secret");
        _upstream.AddImage("team/secret", "1.0", "s");
        await CreateRemoteAsync();

        using var response = await GetAsync(ManifestPath("team/secret", "1.0"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var scope = _app.Services.CreateScope();
        var cache = await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().GetCacheAsync(Remote, Cancel);
        Assert.Null(cache.Status.FailingSinceUtc);
    }

    [Fact]
    public async Task AWrongPasswordIs502AndTheStatusSaysSoWithoutTheSecret()
    {
        _upstream.TokenCredentials = "puller:s3cret";
        _upstream.AddImage("team/app", "1.0", "a");
        await CreateRemoteAsync(s =>
        {
            s.Username = "puller";
            s.Password = "wrong-password";
        });

        using var response = await GetAsync(ManifestPath("team/app", "1.0"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Cancel);
        Assert.Contains("refused the credentials", body);
        Assert.DoesNotContain("wrong-password", body);

        using var scope = _app.Services.CreateScope();
        var cache = await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().GetCacheAsync(Remote, Cancel);
        Assert.NotNull(cache.Status.FailingSinceUtc);
        Assert.DoesNotContain("wrong-password", cache.Status.LastError);
    }

    [Fact]
    public async Task ARegistryThatAsksForBasicIsAnsweredWithTheCredentialsAtItsOwnHost()
    {
        _upstream.TokenAuth = false;
        _upstream.RequiredBasic = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("puller:s3cret"));
        _upstream.AddImage("team/app", "1.0", "a");
        await CreateRemoteAsync(s =>
        {
            s.Username = "puller";
            s.Password = "s3cret";
        });

        using var response = await GetAsync(ManifestPath("team/app", "1.0"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, _upstream.TokenRequests);
    }

    [Fact]
    public async Task APasswordWithoutAUserNameIsSentAsABearerTokenToTheRegistry()
    {
        _upstream.TokenAuth = false;
        _upstream.AddImage("team/app", "1.0", "a");
        await CreateRemoteAsync(s => s.Password = "pre-made-token");

        using var response = await GetAsync(ManifestPath("team/app", "1.0"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(_upstream.Requests, r => r.Host == FakeOciUpstream.RegistryHost && r.Authorization == "Bearer pre-made-token");
    }

    [Fact]
    public async Task APrivateRemoteRepositoryAsksNothingOfItsUpstreamForAnAnonymousCaller()
    {
        _upstream.AddImage("team/app", "1.0", "a");
        await CreateRemoteAsync(anonymousRead: false);

        using var response = await GetAsync(ManifestPath("team/app", "1.0"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_upstream.Requests);
    }

    // ---- Docker Hub ----

    [Fact]
    public async Task AnOfficialImageAtDockerHubIsAskedForUnderLibrary()
    {
        _upstream.AddImage("library/nginx", "latest", "n");
        await CreateRemoteAsync(s => s.Url = "https://registry-1.docker.io");

        using var response = await GetAsync(ManifestPath("nginx", "latest"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(_upstream.Requests, r => r.Method == "GET" && r.Path == "v2/library/nginx/manifests/latest");
        Assert.DoesNotContain(_upstream.Requests, r => r.Path.StartsWith("v2/nginx/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnImageWithANamespaceAtDockerHubIsAskedForAsItIs()
    {
        _upstream.AddImage("grafana/grafana", "latest", "g");
        await CreateRemoteAsync(s => s.Url = "https://registry-1.docker.io");

        using var response = await GetAsync(ManifestPath("grafana/grafana", "latest"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(_upstream.Requests, r => r.Path.Contains("library/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnotherRegistrysSingleNameIsNotMovedUnderLibrary()
    {
        _upstream.AddImage("tool", "1", "t");
        await CreateRemoteAsync();

        using var response = await GetAsync(ManifestPath("tool", "1"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(_upstream.Requests, r => r.Path.Contains("library/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheRegistryUrlMayEndInV2()
    {
        _upstream.AddImage("team/app", "1.0", "a");
        await CreateRemoteAsync(s => s.Url = UpstreamUrl + "/v2/");

        using var response = await GetAsync(ManifestPath("team/app", "1.0"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(_upstream.Requests, r => r.Path.StartsWith("v2/v2", StringComparison.Ordinal));
    }

    // ---- tags ----

    [Fact]
    public async Task TheTagListIsTheUpstreamsAllPagesAndTheKeptTagsAndPagesThroughHere()
    {
        foreach (var tag in new[] { "1", "2", "3", "4", "5" }) _upstream.AddImage("team/app", tag, "t" + tag);
        await CreateRemoteAsync();

        using var all = await GetAsync($"/v2/{Remote}/team/app/tags/list");
        using var allJson = JsonDocument.Parse(await all.Content.ReadAsStringAsync(Cancel));
        Assert.Equal(new[] { "1", "2", "3", "4", "5" }, allJson.RootElement.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal($"{Remote}/team/app", allJson.RootElement.GetProperty("name").GetString());

        using var page = await GetAsync($"/v2/{Remote}/team/app/tags/list?n=2&last=2");
        using var pageJson = JsonDocument.Parse(await page.Content.ReadAsStringAsync(Cancel));
        Assert.Equal(new[] { "3", "4" }, pageJson.RootElement.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
        Assert.True(page.Headers.Contains("Link"));
    }

    [Fact]
    public async Task TheTagListOfAnImageTheUpstreamDoesNotHaveIs404()
    {
        await CreateRemoteAsync();

        using var response = await GetAsync($"/v2/{Remote}/team/none/tags/list");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("NAME_UNKNOWN", ErrorCode(await response.Content.ReadAsStringAsync(Cancel)));
    }

    // ---- writes ----

    [Fact]
    public async Task APushToARemoteRepositoryIs403DeniedForACallerWhoMayPullAndNothingIsStarted()
    {
        _upstream.AddImage("team/app", "1.0", "a");
        await CreateRemoteAsync();

        using var upload = await SendAsync(HttpMethod.Post, $"/v2/{Remote}/team/app/blobs/uploads/", key: true);
        using var manifest = await SendAsync(HttpMethod.Put, ManifestPath("team/app", "9"), new byte[] { 1 }, key: true);
        using var delete = await SendAsync(HttpMethod.Delete, ManifestPath("team/app", "1.0"), key: true);

        foreach (var response in new[] { upload, manifest, delete })
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("DENIED", ErrorCode(await response.Content.ReadAsStringAsync(Cancel)));
        }

        Assert.Equal((0, 0, 0L), await StoredAsync());
    }

    [Fact]
    public async Task ATokenForARemoteRepositoryCarriesPullAndNeverPush()
    {
        _upstream.AddImage("team/app", "1.0", "a");
        await CreateRemoteAsync();

        using var response = await SendAsync(HttpMethod.Get, $"/v2/token?service=BaGet&scope={Uri.EscapeDataString($"repository:{Remote}/team/app:pull,push,delete")}", key: true);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancel));
        var token = json.RootElement.GetProperty("token").GetString()!;

        using var pull = await SendAsync(HttpMethod.Get, ManifestPath("team/app", "1.0"), configure: r => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token));
        using var push = await SendAsync(HttpMethod.Post, $"/v2/{Remote}/team/app/blobs/uploads/", configure: r => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token));

        Assert.Equal(HttpStatusCode.OK, pull.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, push.StatusCode);
    }

    // ---- administration ----

    [Fact]
    public async Task PurgingAnImageDeletesItsKeptManifestsAndLayersAndForgetsTheAnswers()
    {
        _upstream.AddImage("team/app", "1.0", "a");
        _upstream.AddImage("team/other", "1.0", "b");
        var layer = _upstream.LayersOf("team/app", "1.0")[0];
        await CreateRemoteAsync();
        (await GetAsync(ManifestPath("team/app", "1.0"))).Dispose();
        await (await GetAsync(BlobPath("team/app", layer))).Content.ReadAsByteArrayAsync(Cancel);
        (await GetAsync(ManifestPath("team/other", "1.0"))).Dispose();
        Assert.Equal((2, 2, (await StoredAsync()).Bytes), await StoredAsync());

        using var scope = _app.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().PurgeImageAsync(Remote, "team/app", null, Cancel);

        Assert.Equal(1, result.Packages);
        Assert.True(result.CachedAnswers >= 1);
        Assert.Equal((1, 1, 0L), await StoredAsync());
        Assert.False(await BlobStoredAsync(layer));

        // The answer was forgotten: the next request asks the upstream again.
        var before = _upstream.Count("GET", "team/app/manifests/1.0");
        (await GetAsync(ManifestPath("team/app", "1.0"))).Dispose();
        Assert.Equal(before + 1, _upstream.Count("GET", "team/app/manifests/1.0"));
    }

    [Fact]
    public async Task PurgingOneTagDeletesThatManifestOnly()
    {
        _upstream.AddImage("team/app", "1.0", "a");
        _upstream.AddImage("team/app", "2.0", "b");
        await CreateRemoteAsync();
        (await GetAsync(ManifestPath("team/app", "1.0"))).Dispose();
        (await GetAsync(ManifestPath("team/app", "2.0"))).Dispose();

        using var scope = _app.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().PurgeImageAsync(Remote, "team/app", "1.0", Cancel);

        Assert.Equal(1, result.Packages);
        Assert.Equal((1, 1, 0L), await StoredAsync());
    }

    [Fact]
    public async Task PurgingTheWholeCacheDeletesEveryImage()
    {
        _upstream.AddImage("team/app", "1.0", "a");
        _upstream.AddImage("team/other", "1.0", "b");
        await CreateRemoteAsync();
        (await GetAsync(ManifestPath("team/app", "1.0"))).Dispose();
        (await GetAsync(ManifestPath("team/other", "1.0"))).Dispose();

        using var scope = _app.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().PurgeAsync(Remote, Cancel);

        Assert.Equal(2, result.Packages);
        Assert.Equal((0, 0, 0L), await StoredAsync());
    }

    [Fact]
    public async Task ARepositoryThatIsNotAnImageNameIsRefusedByThePurge()
    {
        await CreateRemoteAsync();
        using var scope = _app.Services.CreateScope();

        await Assert.ThrowsAsync<ArgumentException>(() => scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().PurgeImageAsync(Remote, "Not An Image", null, Cancel));
        await Assert.ThrowsAsync<ArgumentException>(() => scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().PurgeImageAsync(Remote, "team/app", "not a tag!", Cancel));
    }

    [Fact]
    public async Task TestConnectionPingsTheRegistryThroughTheTokenFlow()
    {
        await CreateRemoteAsync();
        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();

        var result = await admin.TestConnectionAsync(Remote, Settings(), Cancel);

        Assert.True(result.Success, result.Message);
        Assert.Contains("registry answered", result.Message);
    }

    [Fact]
    public async Task TestConnectionWithAWrongPasswordSaysTheTokenServiceRefusedIt()
    {
        _upstream.TokenCredentials = "puller:s3cret";
        _upstream.TokenServiceRefuses = true;
        await CreateRemoteAsync();
        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();

        var result = await admin.TestConnectionAsync(Remote, Settings(s => { s.Username = "puller"; s.Password = "nope"; }), Cancel);

        Assert.False(result.Success);
        Assert.Contains("token service refused the credentials", result.Message);
        Assert.DoesNotContain("nope", result.Message);
    }

    [Fact]
    public async Task TestConnectionToSomethingThatIsNotARegistryIsFalse()
    {
        _upstream.FailWith = HttpStatusCode.NotFound;
        await CreateRemoteAsync();
        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();

        var result = await admin.TestConnectionAsync(Remote, Settings(), Cancel);

        Assert.False(result.Success);
        Assert.Contains("not a Docker registry", result.Message);
    }

    [Fact]
    public async Task TestConnectionDoesNotDisturbTheLiveTransportOfTheRepository()
    {
        _upstream.AddImage("team/app", "1.0", "a");
        await CreateRemoteAsync(s => s.MetadataTtlSeconds = 0);
        (await GetAsync(ManifestPath("team/app", "1.0"))).Dispose();
        var tokens = _upstream.TokenRequests;

        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().TestConnectionAsync(Remote, Settings(s => s.DownloadTimeoutSeconds = 5), Cancel);
        }

        // The repository's own token is still good: the probe had a state of its own.
        (await GetAsync(ManifestPath("team/app", "1.0"))).Dispose();
        Assert.Equal(tokens + 1, _upstream.TokenRequests);
    }

    // ---- pages ----

    [Fact]
    public async Task TheBrowsePageListsTheKeptImagesAndSaysTheRepositoryTakesNoPushes()
    {
        _upstream.AddImage("team/app", "1.0", "a");
        await CreateRemoteAsync();
        (await GetAsync(ManifestPath("team/app", "1.0"))).Dispose();

        using var browse = await GetAsync($"/r/{Remote}/docker-images");
        var html = await browse.Content.ReadAsStringAsync(Cancel);

        Assert.Equal(HttpStatusCode.OK, browse.StatusCode);
        Assert.Contains("team/app", html);
        Assert.Contains("caches an upstream registry", html);
        Assert.DoesNotContain("docker push", html);
        Assert.DoesNotContain("buildx", html);

        using var image = await GetAsync($"/r/{Remote}/docker-images/team/app");
        var imageHtml = await image.Content.ReadAsStringAsync(Cancel);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.DoesNotContain("docker push", imageHtml);
        Assert.DoesNotContain("docker-manage-link", imageHtml);
    }

    [Fact]
    public async Task TheManagePageOfARemoteRepositoryIs404()
    {
        _upstream.AddImage("team/app", "1.0", "a");
        await CreateRemoteAsync();
        (await GetAsync(ManifestPath("team/app", "1.0"))).Dispose();

        using var response = await GetAsync($"/r/{Remote}/docker-manage/team/app");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
