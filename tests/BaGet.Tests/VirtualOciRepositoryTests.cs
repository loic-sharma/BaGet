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

/// <summary>
/// Virtual Docker repositories (P5): one registry made of local and remote members in resolution order. Local members are pushed to over
/// HTTP; the remote member's upstream is a <see cref="FakeOciUpstream"/>. The real Docker client is in <c>BaGet.Clients.Tests</c>.
/// </summary>
public class VirtualOciRepositoryTests : IDisposable
{
    private const string Key = "secret-key";
    private const string UpstreamUrl = "https://" + FakeOciUpstream.RegistryHost;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly FakeOciUpstream _upstream = new();
    private readonly ForwardingRemoteHttpClientFactory _wire;
    private readonly ManualTimeProvider _time = new();
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public VirtualOciRepositoryTests(ITestOutputHelper output)
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

    private static AuthenticationHeaderValue Basic() => new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("ci:" + Key)));

    private async Task CreateLocalAsync(string name, bool anonymousRead = true)
        => await _app.CreateDockerRepositoryAsync(name, anonymousRead, Cancel);

    private async Task CreateRemoteAsync(string name = "hub", Action<RemoteRepositorySettings>? change = null)
    {
        var settings = new RemoteRepositorySettings { Url = UpstreamUrl, Protocol = RemoteProtocol.V3, MetadataTtlSeconds = 600, NegativeTtlSeconds = 60 };
        change?.Invoke(settings);
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
            .CreateAsync(name, RepositoryFormat.Docker, RepositoryKind.Remote, settings, anonymousRead: true, Cancel);
    }

    private async Task CreateVirtualAsync(string name, string[] members, string? deploy, bool anonymousRead = true)
    {
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>().CreateAsync(
            name,
            new VirtualRepositorySettings { Members = members.ToList(), DeployRepository = deploy },
            Cancel,
            actor: null,
            anonymousRead,
            RepositoryFormat.Docker);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, byte[]? body = null, string? contentType = null, bool key = false, string? bearer = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body != null)
        {
            request.Content = new ByteArrayContent(body);
            if (contentType != null) request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        if (key) request.Headers.Authorization = Basic();
        else if (bearer != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

        return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Cancel);
    }

    private async Task PushBlobAsync(string repo, string image, byte[] content)
    {
        using var start = await SendAsync(HttpMethod.Post, $"/v2/{repo}/{image}/blobs/uploads/", key: true);
        Assert.True(start.StatusCode == HttpStatusCode.Accepted, $"POST upload: {start.StatusCode} {await start.Content.ReadAsStringAsync(Cancel)}");
        var location = start.Headers.Location!.ToString();
        using var finish = await SendAsync(HttpMethod.Put, location + (location.Contains('?') ? "&" : "?") + "digest=" + Uri.EscapeDataString(OciTestImage.Digest(content)), content, "application/octet-stream", key: true);
        Assert.True(finish.StatusCode == HttpStatusCode.Created, $"PUT blob: {finish.StatusCode} {await finish.Content.ReadAsStringAsync(Cancel)}");
    }

    private async Task<OciTestImage> PushImageAsync(string repo, string image, string tag, string seed)
    {
        var content = OciTestImage.Create(seed);
        await PushBlobAsync(repo, image, content.Config);
        foreach (var layer in content.Layers) await PushBlobAsync(repo, image, layer);

        using var put = await SendAsync(HttpMethod.Put, $"/v2/{repo}/{image}/manifests/{tag}", content.Manifest, OciTestImage.ManifestType, key: true);
        Assert.True(put.StatusCode == HttpStatusCode.Created, $"PUT manifest: {put.StatusCode} {await put.Content.ReadAsStringAsync(Cancel)}");

        return content;
    }

    private async Task<string?> DigestOfAsync(string repo, string image, string tag, bool key = false)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/v2/{repo}/{image}/manifests/{tag}", key: key);

        return response.StatusCode == HttpStatusCode.OK ? response.Headers.GetValues("Docker-Content-Digest").Single() : null;
    }

    private async Task<bool> BlobStoredAsync(string repository, string digest)
    {
        using var scope = await _app.CreateRepositoryScopeAsync(repository, Cancel);

        return await scope.ServiceProvider.GetRequiredService<OciRegistryService>().FindRepositoryBlobAsync(digest, Cancel) != null;
    }

    private async Task<int> ManifestsOfAsync(string repository)
    {
        using var scope = _app.Services.CreateScope();

        return (await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().GetCacheAsync(repository, Cancel)).PackageCount;
    }

    private static string ErrorCode(string body)
    {
        using var json = JsonDocument.Parse(body);

        return json.RootElement.GetProperty("errors")[0].GetProperty("code").GetString()!;
    }

    // ---- creation ----

    [Fact]
    public async Task ADockerRepositoryCanBeVirtualAndItsMembersAreDockerRepositories()
    {
        await CreateLocalAsync("local");
        await CreateRemoteAsync();
        await CreateVirtualAsync("all", new[] { "local", "hub" }, "local");

        using var scope = _app.Services.CreateScope();
        var detail = await scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>().FindAsync("all", Cancel);

        Assert.Equal(RepositoryFormat.Docker, detail!.Repository.Format);
        Assert.Equal(new[] { "local", "hub" }, detail.Members.Select(m => m.Name));
        Assert.Equal("local", detail.DeployTarget!.Name);

        // A remote repository takes no pushes, so it cannot be where they go.
        await Assert.ThrowsAsync<ArgumentException>(() => CreateVirtualAsync("bad", new[] { "local", "hub" }, "hub"));
    }

    // ---- reads ----

    [Fact]
    public async Task ATagIsTheFirstMembersThatHasIt()
    {
        await CreateLocalAsync("local");
        await CreateRemoteAsync();
        var mine = await PushImageAsync("local", "team/app", "1.0", "mine");
        var theirs = _upstream.AddImage("team/app", "1.0", "theirs");
        await CreateVirtualAsync("localfirst", new[] { "local", "hub" }, "local");
        await CreateVirtualAsync("hubfirst", new[] { "hub", "local" }, "local");

        Assert.Equal(mine.ManifestDigest, await DigestOfAsync("localfirst", "team/app", "1.0"));
        Assert.Equal(theirs, await DigestOfAsync("hubfirst", "team/app", "1.0"));
    }

    [Fact]
    public async Task AnImageOnlyALaterMemberHasIsFoundThereAndARemoteMemberKeepsWhatItFetched()
    {
        await CreateLocalAsync("local");
        await CreateRemoteAsync();
        var digest = _upstream.AddImage("team/app", "1.0", "theirs");
        await CreateVirtualAsync("all", new[] { "local", "hub" }, "local");

        Assert.Equal(digest, await DigestOfAsync("all", "team/app", "1.0"));

        Assert.Equal(1, await ManifestsOfAsync("hub"));
        using var scope = await _app.CreateRepositoryScopeAsync("local", Cancel);
        Assert.Null(await scope.ServiceProvider.GetRequiredService<OciRegistryService>().FindManifestAsync("team/app", "1.0", Cancel));
    }

    [Fact]
    public async Task AnUnknownImageIs404InTheRegistrysFormat()
    {
        await CreateLocalAsync("local");
        await CreateRemoteAsync();
        await CreateVirtualAsync("all", new[] { "local", "hub" }, "local");

        using var response = await SendAsync(HttpMethod.Get, "/v2/all/team/none/manifests/1.0");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("MANIFEST_UNKNOWN", ErrorCode(await response.Content.ReadAsStringAsync(Cancel)));
    }

    [Fact]
    public async Task ALayerIsServedByTheMemberThatHoldsItWithItsSizeAndDigest()
    {
        await CreateLocalAsync("local");
        await CreateRemoteAsync();
        var image = await PushImageAsync("local", "team/app", "1.0", "mine");
        await CreateVirtualAsync("all", new[] { "hub", "local" }, "local");
        var layer = image.LayerDigests[0];

        using var head = await SendAsync(HttpMethod.Head, $"/v2/all/team/app/blobs/{layer}");
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(image.Layers[0].Length, head.Content.Headers.ContentLength);

        using var get = await SendAsync(HttpMethod.Get, $"/v2/all/team/app/blobs/{layer}");
        Assert.Equal(image.Layers[0], await get.Content.ReadAsByteArrayAsync(Cancel));
        Assert.Equal(layer, get.Headers.GetValues("Docker-Content-Digest").Single());
    }

    [Fact]
    public async Task ALayerOfARemoteMemberIsStreamedThroughTheVirtualRepositoryAndKeptByTheMember()
    {
        await CreateLocalAsync("local");
        await CreateRemoteAsync();
        _upstream.AddImage("team/app", "1.0", "theirs", layerSize: 4000);
        var layer = _upstream.LayersOf("team/app", "1.0")[0];
        await CreateVirtualAsync("all", new[] { "local", "hub" }, "local");
        (await SendAsync(HttpMethod.Get, "/v2/all/team/app/manifests/1.0")).Dispose();

        using var response = await SendAsync(HttpMethod.Get, $"/v2/all/team/app/blobs/{layer}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(_upstream.Blob(layer), await response.Content.ReadAsByteArrayAsync(Cancel));
        Assert.True(await BlobStoredAsync("hub", layer));
        Assert.False(await BlobStoredAsync("local", layer));
    }

    [Fact]
    public async Task TheTagListIsTheMembersTagsMergedAndPaged()
    {
        await CreateLocalAsync("local");
        await CreateRemoteAsync();
        await PushImageAsync("local", "team/app", "1.0", "a");
        await PushImageAsync("local", "team/app", "2.0", "b");
        _upstream.AddImage("team/app", "2.0", "other");
        _upstream.AddImage("team/app", "3.0", "c");
        await CreateVirtualAsync("all", new[] { "local", "hub" }, "local");

        using var all = await SendAsync(HttpMethod.Get, "/v2/all/team/app/tags/list");
        using var json = JsonDocument.Parse(await all.Content.ReadAsStringAsync(Cancel));
        Assert.Equal(new[] { "1.0", "2.0", "3.0" }, json.RootElement.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal("all/team/app", json.RootElement.GetProperty("name").GetString());

        using var page = await SendAsync(HttpMethod.Get, "/v2/all/team/app/tags/list?n=1&last=1.0");
        using var pageJson = JsonDocument.Parse(await page.Content.ReadAsStringAsync(Cancel));
        Assert.Equal(new[] { "2.0" }, pageJson.RootElement.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
    }

    [Fact]
    public async Task AMemberThatIsDownIsSkippedAndTheNextMemberAnswers()
    {
        await CreateLocalAsync("local");
        await CreateRemoteAsync();
        var mine = await PushImageAsync("local", "team/app", "1.0", "mine");
        await CreateVirtualAsync("all", new[] { "hub", "local" }, "local");
        _wire.Down = true;

        Assert.Equal(mine.ManifestDigest, await DigestOfAsync("all", "team/app", "1.0"));
    }

    [Fact]
    public async Task AVirtualRepositoryWithNoMembersIsEmpty()
    {
        await CreateVirtualAsync("empty", Array.Empty<string>(), null);

        using var response = await SendAsync(HttpMethod.Get, "/v2/empty/team/app/manifests/1.0");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- access ----

    [Fact]
    public async Task AMemberTheCallerMayNotReadIsNotAskedForThem()
    {
        await CreateLocalAsync("secret", anonymousRead: false);
        await CreateLocalAsync("open");
        var hidden = await PushImageAsync("secret", "team/app", "1.0", "hidden");
        await CreateVirtualAsync("all", new[] { "secret", "open" }, "open");

        Assert.Null(await DigestOfAsync("all", "team/app", "1.0"));
        Assert.Equal(hidden.ManifestDigest, await DigestOfAsync("all", "team/app", "1.0", key: true));

        using var blob = await SendAsync(HttpMethod.Get, $"/v2/all/team/app/blobs/{hidden.LayerDigests[0]}");
        Assert.Equal(HttpStatusCode.NotFound, blob.StatusCode);
    }

    [Fact]
    public async Task APrivateVirtualRepositoryChallengesAnAnonymousCaller()
    {
        await CreateLocalAsync("local");
        await CreateVirtualAsync("all", new[] { "local" }, "local", anonymousRead: false);

        using var response = await SendAsync(HttpMethod.Get, "/v2/all/team/app/manifests/1.0");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ATokenForAVirtualRepositoryCarriesThePullOfEachMemberTheCallerMayPull()
    {
        await CreateLocalAsync("secret", anonymousRead: false);
        await CreateLocalAsync("open");
        var hidden = await PushImageAsync("secret", "team/app", "1.0", "hidden");
        await CreateVirtualAsync("all", new[] { "secret", "open" }, "open");

        async Task<string> TokenAsync(bool withKey)
        {
            using var response = await SendAsync(HttpMethod.Get, "/v2/token?service=BaGet&scope=" + Uri.EscapeDataString("repository:all/team/app:pull"), key: withKey);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancel));

            return json.RootElement.GetProperty("token").GetString()!;
        }

        // docker login with the key: the token reaches the secret member through the virtual repository.
        using (var withKey = await SendAsync(HttpMethod.Get, "/v2/all/team/app/manifests/1.0", bearer: await TokenAsync(withKey: true)))
        {
            Assert.Equal(HttpStatusCode.OK, withKey.StatusCode);
            Assert.Equal(hidden.ManifestDigest, withKey.Headers.GetValues("Docker-Content-Digest").Single());
        }

        // An anonymous token does not.
        using var anonymous = await SendAsync(HttpMethod.Get, "/v2/all/team/app/manifests/1.0", bearer: await TokenAsync(withKey: false));
        Assert.Equal(HttpStatusCode.NotFound, anonymous.StatusCode);
    }

    // ---- writes ----

    [Fact]
    public async Task APushToAVirtualRepositoryLandsInItsDeployRepositoryAndOnlyThere()
    {
        await CreateLocalAsync("local");
        await CreateLocalAsync("other");
        await CreateRemoteAsync();
        await CreateVirtualAsync("all", new[] { "other", "hub", "local" }, "local");

        var pushed = await PushImageAsync("all", "team/app", "1.0", "pushed");

        Assert.Equal(pushed.ManifestDigest, await DigestOfAsync("local", "team/app", "1.0"));
        Assert.Null(await DigestOfAsync("other", "team/app", "1.0"));
        Assert.Equal(0, await ManifestsOfAsync("hub"));
        Assert.Equal(pushed.ManifestDigest, await DigestOfAsync("all", "team/app", "1.0"));

        // The URLs the registry handed out stayed in the repository the client used.
        using var start = await SendAsync(HttpMethod.Post, "/v2/all/team/app/blobs/uploads/", key: true);
        Assert.StartsWith("/v2/all/team/app/blobs/uploads/", start.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task APushToAVirtualRepositoryWithoutADeployRepositoryIs403Denied()
    {
        await CreateLocalAsync("local");
        await CreateVirtualAsync("all", new[] { "local" }, null);

        using var response = await SendAsync(HttpMethod.Post, "/v2/all/team/app/blobs/uploads/", key: true);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("DENIED", ErrorCode(await response.Content.ReadAsStringAsync(Cancel)));
    }

    [Fact]
    public async Task ADeleteThroughAVirtualRepositoryRemovesFromTheDeployRepository()
    {
        await CreateLocalAsync("local");
        await CreateVirtualAsync("all", new[] { "local" }, "local");
        var pushed = await PushImageAsync("all", "team/app", "1.0", "pushed");

        using var delete = await SendAsync(HttpMethod.Delete, $"/v2/all/team/app/manifests/{pushed.ManifestDigest}", key: true);

        Assert.Equal(HttpStatusCode.Accepted, delete.StatusCode);
        Assert.Null(await DigestOfAsync("local", "team/app", "1.0"));
    }

    [Fact]
    public async Task APushTokenForAVirtualRepositoryIsGrantedByTheDeployRepository()
    {
        await CreateLocalAsync("local");
        await CreateVirtualAsync("all", new[] { "local" }, "local");

        using var response = await SendAsync(HttpMethod.Get, "/v2/token?service=BaGet&scope=" + Uri.EscapeDataString("repository:all/team/app:pull,push"), key: true);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancel));
        var token = json.RootElement.GetProperty("token").GetString()!;

        using var start = await SendAsync(HttpMethod.Post, "/v2/all/team/app/blobs/uploads/", bearer: token);

        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
    }

    [Fact]
    public async Task AMountThroughAVirtualRepositoryNeedsThePullOfTheSourceInTheDeployRepository()
    {
        await CreateLocalAsync("priv");
        await CreateVirtualAsync("v", new[] { "priv" }, "priv");
        var hidden = await PushImageAsync("priv", "secret/app", "1.0", "hidden");
        var layer = OciTestImage.Digest(hidden.Layers[0]);
        var tokens = _app.Services.CreateScope().ServiceProvider.GetRequiredService<IOciTokenService>();
        var mountUri = $"/v2/v/u/app/blobs/uploads/?mount={Uri.EscapeDataString(layer)}&from=v/secret/app";

        // Grants on the virtual name alone (what the token endpoint hands out from Read on the virtual repository).
        var viaVirtualOnly = tokens.Issue("u", new[]
        {
            new OciGrant("v", "secret/app", OciActions.Pull),
            new OciGrant("v", "u/app", OciActions.Pull | OciActions.Push),
            new OciGrant("priv", "u/app", OciActions.Pull | OciActions.Push),
        }).Token;
        using (var refused = await SendAsync(HttpMethod.Post, mountUri, bearer: viaVirtualOnly))
        {
            Assert.Equal(HttpStatusCode.Accepted, refused.StatusCode);
        }

        var withMember = tokens.Issue("u", new[]
        {
            new OciGrant("v", "secret/app", OciActions.Pull),
            new OciGrant("v", "u/app", OciActions.Pull | OciActions.Push),
            new OciGrant("priv", "secret/app", OciActions.Pull),
            new OciGrant("priv", "u/app", OciActions.Pull | OciActions.Push),
        }).Token;
        using var mounted = await SendAsync(HttpMethod.Post, mountUri, bearer: withMember);
        Assert.Equal(HttpStatusCode.Created, mounted.StatusCode);
    }

    // ---- administration and pages ----

    [Fact]
    public async Task TheResolutionPreviewAsksEachMemberForATagOrADigest()
    {
        await CreateLocalAsync("local");
        await CreateRemoteAsync();
        await CreateLocalAsync("third");
        await PushImageAsync("local", "team/app", "1.0", "mine");
        _upstream.AddImage("team/app", "2.0", "theirs");
        await CreateVirtualAsync("all", new[] { "local", "hub", "third" }, "local");
        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>();

        var stored = await admin.PreviewAsync("all", "team/app", "1.0", Cancel);
        Assert.Equal("local", stored.Answers!.Name);
        Assert.Equal(
            new[] { ResolutionOutcome.Stored, ResolutionOutcome.NotAsked, ResolutionOutcome.NotAsked },
            stored.Steps.Select(s => s.Outcome));

        var fetched = await admin.PreviewAsync("all", "team/app", "2.0", Cancel);
        Assert.Equal("hub", fetched.Answers!.Name);
        Assert.Equal(
            new[] { ResolutionOutcome.NotFound, ResolutionOutcome.WouldFetch, ResolutionOutcome.NotAsked },
            fetched.Steps.Select(s => s.Outcome));

        var missing = await admin.PreviewAsync("all", "team/app", "9.9", Cancel);
        Assert.Null(missing.Answers);
        Assert.Equal(0, await ManifestsOfAsync("hub"));

        await Assert.ThrowsAsync<ArgumentException>(() => admin.PreviewAsync("all", "Not An Image", "1.0", Cancel));
        await Assert.ThrowsAsync<ArgumentException>(() => admin.PreviewAsync("all", "team/app", "not a tag!", Cancel));
    }

    [Fact]
    public async Task TheBrowsePageMergesTheMembersImages()
    {
        await CreateLocalAsync("local");
        await CreateRemoteAsync();
        await PushImageAsync("local", "team/mine", "1.0", "a");
        _upstream.AddImage("team/theirs", "1.0", "b");
        await CreateVirtualAsync("all", new[] { "local", "hub" }, "local");
        (await SendAsync(HttpMethod.Get, "/v2/all/team/theirs/manifests/1.0")).Dispose();

        using var browse = await SendAsync(HttpMethod.Get, "/r/all/docker-images");
        var html = await browse.Content.ReadAsStringAsync(Cancel);

        Assert.Equal(HttpStatusCode.OK, browse.StatusCode);
        Assert.Contains("team/mine", html);
        Assert.Contains("team/theirs", html);
        Assert.Contains("merges other repositories", html);
    }
}
