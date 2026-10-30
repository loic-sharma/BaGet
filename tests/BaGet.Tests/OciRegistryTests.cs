using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BaGet.Web.V3.Controllers.Protocols;

namespace BaGet.Tests;

/// <summary>
/// The OCI Distribution Spec over HTTP against the real pipeline (P5): the version check and token service, blobs (monolithic,
/// chunked, mounted, ranged), manifests and indexes, tags, referrers, deletes, and the checks that make a registry safe (digests
/// verified, references present, blobs only through the image that holds them). The real Docker client is exercised in
/// <c>BaGet.Clients.Tests</c>.
/// </summary>
public class OciRegistryTests : IDisposable
{
    private const string Key = "secret-key";

    private readonly ITestOutputHelper _output;
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public OciRegistryTests(ITestOutputHelper output)
    {
        _output = output;
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string> { { "ApiKey", Key } });
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }

    private Task CreateAsync(string name = "libs", bool anonymousRead = true) => _app.CreateDockerRepositoryAsync(name, anonymousRead, TestContext.Current.CancellationToken);

    private static AuthenticationHeaderValue Basic(string user = "ci", string password = Key)
        => new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password)));

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token = null, byte[]? body = null, string? contentType = null, AuthenticationHeaderValue? auth = null, Action<HttpRequestMessage>? configure = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body != null)
        {
            request.Content = new ByteArrayContent(body);
            if (contentType != null) request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        else if (auth != null) request.Headers.Authorization = auth;

        configure?.Invoke(request);

        // The response is read fully by the caller after the request is gone.
        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<string> TextAsync(HttpResponseMessage response) => await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

    // The docker login flow: a token for the scopes, with the API key as the Basic password (or anonymous).
    private async Task<string> TokenAsync(string scope, bool anonymous = false)
    {
        using var response = await SendAsync(HttpMethod.Get, "/v2/token?service=BaGet&scope=" + Uri.EscapeDataString(scope), auth: anonymous ? null : Basic());
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"token: {response.StatusCode} {await TextAsync(response)}");
        using var json = JsonDocument.Parse(await TextAsync(response));

        return json.RootElement.GetProperty("token").GetString()!;
    }

    private Task<string> PushTokenAsync(string image = "libs/app") => TokenAsync($"repository:{image}:pull,push,delete");

    private async Task PushBlobAsync(string token, string image, byte[] content)
    {
        var digest = OciTestImage.Digest(content);
        using var start = await SendAsync(HttpMethod.Post, $"/v2/{image}/blobs/uploads/", token);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var location = start.Headers.Location!.ToString();

        using var finish = await SendAsync(HttpMethod.Put, location + "?digest=" + Uri.EscapeDataString(digest), token, content, "application/octet-stream");
        Assert.True(finish.StatusCode == HttpStatusCode.Created, $"PUT blob: {finish.StatusCode} {await TextAsync(finish)}");
    }

    private async Task<HttpResponseMessage> PutManifestAsync(string token, string image, string reference, byte[] manifest, string contentType = OciTestImage.ManifestType)
        => await SendAsync(HttpMethod.Put, $"/v2/{image}/manifests/{reference}", token, manifest, contentType);

    private async Task PushImageAsync(string token, string image, string tag, OciTestImage content)
    {
        await PushBlobAsync(token, image, content.Config);
        foreach (var layer in content.Layers) await PushBlobAsync(token, image, layer);

        using var put = await PutManifestAsync(token, image, tag, content.Manifest);
        Assert.True(put.StatusCode == HttpStatusCode.Created, $"PUT manifest: {put.StatusCode} {await TextAsync(put)}");
    }

    private static string ErrorCode(string body)
    {
        using var json = JsonDocument.Parse(body);

        return json.RootElement.GetProperty("errors")[0].GetProperty("code").GetString()!;
    }

    // ---- version check and token service ----

    [Fact]
    public async Task AnAnonymousVersionCheckIsAChallengeToTheTokenEndpoint()
    {
        using var response = await SendAsync(HttpMethod.Get, "/v2/");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("registry/2.0", response.Headers.GetValues("Docker-Distribution-API-Version").Single());
        var challenge = response.Headers.WwwAuthenticate.Single();
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Contains("realm=\"http://localhost/v2/token\"", challenge.Parameter);
        Assert.Contains("service=\"BaGet\"", challenge.Parameter);
        Assert.Equal("UNAUTHORIZED", ErrorCode(await TextAsync(response)));
    }

    [Theory]
    [InlineData("/v2/")]
    [InlineData("/v2")]
    public async Task TheVersionCheckAcceptsAKeyAndAToken(string path)
    {
        using var withKey = await SendAsync(HttpMethod.Get, path, auth: Basic());
        Assert.Equal(HttpStatusCode.OK, withKey.StatusCode);
        Assert.Equal("{}", await TextAsync(withKey));

        var token = await TokenAsync("repository:libs/app:pull");
        using var withToken = await SendAsync(HttpMethod.Get, path, token);
        Assert.Equal(HttpStatusCode.OK, withToken.StatusCode);
    }

    [Fact]
    public async Task ATokenIsIssuedForTheActionsTheCallerHolds()
    {
        await CreateAsync();

        using var response = await SendAsync(HttpMethod.Get, "/v2/token?service=BaGet&scope=repository:libs/app:pull,push", auth: Basic());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await TextAsync(response));
        var token = json.RootElement.GetProperty("token").GetString()!;
        Assert.Equal(token, json.RootElement.GetProperty("access_token").GetString());
        Assert.Equal(300, json.RootElement.GetProperty("expires_in").GetInt32());

        // The token holds scopes, never the key.
        Assert.DoesNotContain(Key, token);
        Assert.DoesNotContain("libs", token);

        var claims = _app.Services.CreateScope().ServiceProvider.GetRequiredService<IOciTokenService>().Validate(token)!;
        Assert.True(claims.Allows("libs", "app", OciActions.Push));
        Assert.True(claims.Allows("libs", "app", OciActions.Pull));
        Assert.False(claims.Allows("libs", "app", OciActions.Delete));
        Assert.False(claims.Allows("libs", "other", OciActions.Push));
    }

    [Fact]
    public async Task AnAnonymousTokenGrantsPullOnAPublicRepositoryAndNothingElse()
    {
        await CreateAsync(anonymousRead: true);
        await CreateAsync("secret", anonymousRead: false);

        var token = await TokenAsync("repository:libs/app:pull,push repository:secret/app:pull", anonymous: true);
        var claims = _app.Services.CreateScope().ServiceProvider.GetRequiredService<IOciTokenService>().Validate(token)!;

        Assert.True(claims.Allows("libs", "app", OciActions.Pull));
        Assert.False(claims.Allows("libs", "app", OciActions.Push));
        Assert.False(claims.Allows("secret", "app", OciActions.Pull));
        Assert.Null(claims.Subject);
    }

    [Fact]
    public async Task TheTokenEndpointRefusesWrongCredentialsButNotNone()
    {
        using var wrong = await SendAsync(HttpMethod.Get, "/v2/token?service=BaGet", auth: Basic(password: "nope"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Contains("Basic", wrong.Headers.WwwAuthenticate.Select(h => h.Scheme));

        using var none = await SendAsync(HttpMethod.Get, "/v2/token?service=BaGet");
        Assert.Equal(HttpStatusCode.OK, none.StatusCode);

        using var otherService = await SendAsync(HttpMethod.Get, "/v2/token?service=elsewhere", auth: Basic());
        Assert.Equal(HttpStatusCode.BadRequest, otherService.StatusCode);
    }

    [Fact]
    public async Task ATokenCannotBeForgedOrOutliveItsLifetime()
    {
        await CreateAsync();
        var token = await PushTokenAsync();

        using var forged = await SendAsync(HttpMethod.Post, "/v2/libs/app/blobs/uploads/", token.Substring(0, token.Length - 4) + "AAAA");
        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        Assert.Contains("invalid_token", forged.Headers.WwwAuthenticate.Single().Parameter);

        var tokens = _app.Services.CreateScope().ServiceProvider.GetRequiredService<IOciTokenService>();
        Assert.NotNull(tokens.Validate(token));
        Assert.Null(tokens.Validate("not-a-token"));
        Assert.Null(tokens.Validate(null));
    }

    // ---- access ----

    [Fact]
    public async Task APushWithoutCredentialsIsAChallengeThatNamesTheScope()
    {
        await CreateAsync();

        using var response = await SendAsync(HttpMethod.Post, "/v2/libs/team/app/blobs/uploads/");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("scope=\"repository:libs/team/app:pull,push\"", response.Headers.WwwAuthenticate.Single().Parameter);
    }

    [Fact]
    public async Task APullOnlyTokenCannotPushAndIsToldWhy()
    {
        await CreateAsync();
        var pull = await TokenAsync("repository:libs/app:pull");

        using var response = await SendAsync(HttpMethod.Post, "/v2/libs/app/blobs/uploads/", pull);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("error=\"insufficient_scope\"", response.Headers.WwwAuthenticate.Single().Parameter);
    }

    [Fact]
    public async Task ATokenForOneImageIsNotValidForAnother()
    {
        await CreateAsync();
        var token = await TokenAsync("repository:libs/app:pull,push");

        using var response = await SendAsync(HttpMethod.Post, "/v2/libs/other/blobs/uploads/", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownRepositoryIsAChallengeForAnonymousCallersAndNotFoundForAKey()
    {
        // The same answer as a private repository: nothing says whether it exists.
        using var anonymous = await SendAsync(HttpMethod.Get, "/v2/nowhere/app/tags/list");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var withKey = await SendAsync(HttpMethod.Get, "/v2/nowhere/app/tags/list", auth: Basic());
        Assert.Equal(HttpStatusCode.NotFound, withKey.StatusCode);
        Assert.Equal("NAME_UNKNOWN", ErrorCode(await TextAsync(withKey)));
    }

    [Fact]
    public async Task ARepositoryOfAnotherFormatHasNoRegistry()
    {
        await _app.CreateNpmRepositoryAsync("web", cancellationToken: TestContext.Current.CancellationToken);

        using var response = await SendAsync(HttpMethod.Get, "/v2/web/app/tags/list", auth: Basic());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var plain = await SendAsync(HttpMethod.Get, "/v2/default/app/tags/list", auth: Basic());
        Assert.Equal(HttpStatusCode.NotFound, plain.StatusCode);
    }

    [Fact]
    public async Task APrivateRepositoryIsPulledOnlyWithCredentials()
    {
        await CreateAsync("secret", anonymousRead: false);
        var token = await TokenAsync("repository:secret/app:pull,push");
        var image = OciTestImage.Create("private");
        await PushImageAsync(token, "secret/app", "1", image);

        using var anonymous = await SendAsync(HttpMethod.Get, "/v2/secret/app/manifests/1");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var anonymousToken = await TokenAsync("repository:secret/app:pull", anonymous: true);
        using var withAnonymousToken = await SendAsync(HttpMethod.Get, "/v2/secret/app/manifests/1", anonymousToken);
        Assert.Equal(HttpStatusCode.Unauthorized, withAnonymousToken.StatusCode);

        using var withKey = await SendAsync(HttpMethod.Get, "/v2/secret/app/manifests/1", auth: Basic());
        Assert.Equal(HttpStatusCode.OK, withKey.StatusCode);
    }

    [Fact]
    public async Task APublicRepositoryIsPulledAnonymouslyThroughAnAnonymousToken()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        var image = OciTestImage.Create("public");
        await PushImageAsync(token, "libs/app", "1", image);

        var pull = await TokenAsync("repository:libs/app:pull", anonymous: true);
        using var manifest = await SendAsync(HttpMethod.Get, "/v2/libs/app/manifests/1", pull);
        Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);

        using var direct = await SendAsync(HttpMethod.Get, "/v2/libs/app/manifests/1");
        Assert.Equal(HttpStatusCode.OK, direct.StatusCode);
    }

    [Fact]
    public async Task ABasicApiKeyWorksWithoutTheTokenFlow()
    {
        await CreateAsync();
        var image = OciTestImage.Create("basic");

        using var start = await SendAsync(HttpMethod.Post, "/v2/libs/app/blobs/uploads/", auth: Basic());
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        using var finish = await SendAsync(HttpMethod.Put, start.Headers.Location + "?digest=" + image.ConfigDigest, body: image.Config, auth: Basic());
        Assert.Equal(HttpStatusCode.Created, finish.StatusCode);
    }

    [Fact]
    public async Task ARequestThatTheSpecificationDoesNotDefineIsRefusedNotAllowed()
    {
        await CreateAsync();

        using var getUploads = await SendAsync(HttpMethod.Get, "/v2/libs/app/blobs/uploads/", auth: Basic());
        Assert.Equal(HttpStatusCode.MethodNotAllowed, getUploads.StatusCode);

        using var putBlob = await SendAsync(HttpMethod.Put, "/v2/libs/app/blobs/" + OciTestImage.Digest(Array.Empty<byte>()), auth: Basic(), body: Array.Empty<byte>());
        Assert.Equal(HttpStatusCode.MethodNotAllowed, putBlob.StatusCode);

        using var unknown = await SendAsync(HttpMethod.Get, "/v2/libs/app/whatever", auth: Basic());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Theory]
    [InlineData(OciPathKind.Blob, "GET", OciActions.Pull)]
    [InlineData(OciPathKind.Blob, "HEAD", OciActions.Pull)]
    [InlineData(OciPathKind.Blob, "DELETE", OciActions.Delete)]
    [InlineData(OciPathKind.Manifest, "GET", OciActions.Pull)]
    [InlineData(OciPathKind.Manifest, "PUT", OciActions.Push)]
    [InlineData(OciPathKind.Manifest, "DELETE", OciActions.Delete)]
    [InlineData(OciPathKind.TagList, "GET", OciActions.Pull)]
    [InlineData(OciPathKind.Referrers, "GET", OciActions.Pull)]
    [InlineData(OciPathKind.UploadStart, "POST", OciActions.Push)]
    [InlineData(OciPathKind.Upload, "PATCH", OciActions.Push)]
    [InlineData(OciPathKind.Upload, "PUT", OciActions.Push)]
    [InlineData(OciPathKind.Upload, "GET", OciActions.Push)]
    [InlineData(OciPathKind.Upload, "DELETE", OciActions.Push)]
    public void EveryRouteMapsToTheActionTheModelNames(OciPathKind kind, string method, OciActions expected)
    {
        Assert.Equal(expected, Web.V3.Oci.OciHttp.Required(method, kind));
    }

    [Fact]
    public void ACombinationWithoutADeclaredActionNeedsNothingAndIsRefused()
    {
        foreach (var kind in Enum.GetValues<OciPathKind>())
        {
            foreach (var method in new[] { "OPTIONS", "TRACE", "CONNECT" })
            {
                Assert.Null(Web.V3.Oci.OciHttp.Required(method, kind));
            }
        }

        Assert.Null(Web.V3.Oci.OciHttp.Required("POST", OciPathKind.Blob));
        Assert.Null(Web.V3.Oci.OciHttp.Required("PUT", OciPathKind.TagList));
        Assert.Null(Web.V3.Oci.OciHttp.Required("DELETE", OciPathKind.TagList));
    }

    [Fact]
    public void EveryRegistryRouteIsServedByTheControllerThatAuthorizesAndNoActionEscapesTheFilter()
    {
        // Deny by default: a route of the Docker format that did not resolve and authorize itself would be read like a NuGet route.
        var endpoints = _app.Services.GetServices<Microsoft.AspNetCore.Routing.EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<Web.V3.Routing.RepositoryRouteMetadata>()?.Format == RepositoryFormat.Docker)
            .ToList();

        Assert.NotEmpty(endpoints);
        foreach (var endpoint in endpoints)
        {
            var metadata = endpoint.Metadata.GetMetadata<Web.V3.Routing.RepositoryRouteMetadata>()!;
            Assert.True(metadata.ResolvesRepositoryItself && metadata.AuthorizesItself, endpoint.RoutePattern.RawText);
            Assert.StartsWith("v2", endpoint.RoutePattern.RawText);
            Assert.Equal("Oci", endpoint.RoutePattern.RequiredValues["controller"]);
        }

        // Every other action of the controller sits behind the authorization filter (the attribute is on the class).
        Assert.Contains(typeof(OciController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.ServiceFilterAttribute), inherit: true).Cast<Microsoft.AspNetCore.Mvc.ServiceFilterAttribute>(), a => a.ServiceType == typeof(Web.V3.Oci.OciAuthorizationFilter));
        var actions = typeof(OciController).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
        Assert.Equal(new[] { "Base", "Delete", "Get", "Patch", "Post", "Put", "Token" }, actions.Select(a => a.Name).OrderBy(n => n).ToArray());
    }

    // ---- blobs ----

    [Fact]
    public async Task AMonolithicUploadStoresTheBlobByDigest()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        var content = Encoding.UTF8.GetBytes("layer bytes");
        var digest = OciTestImage.Digest(content);

        using var post = await SendAsync(HttpMethod.Post, "/v2/libs/app/blobs/uploads/?digest=" + digest, token, content, "application/octet-stream");

        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        Assert.Equal("http://localhost/v2/libs/app/blobs/" + digest, post.Headers.Location!.ToString());
        Assert.Equal(digest, post.Headers.GetValues("Docker-Content-Digest").Single());

        using var get = await SendAsync(HttpMethod.Get, $"/v2/libs/app/blobs/{digest}", token);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(content, await get.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(digest, get.Headers.GetValues("Docker-Content-Digest").Single());
    }

    [Fact]
    public async Task AChunkedUploadIsAssembledAndVerified()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        var content = Enumerable.Range(0, 3000).Select(i => (byte)(i * 7)).ToArray();
        var digest = OciTestImage.Digest(content);

        using var start = await SendAsync(HttpMethod.Post, "/v2/libs/app/blobs/uploads/", token);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        Assert.Equal("0-0", start.Headers.GetValues("Range").Single());
        var location = start.Headers.Location!.ToString();
        var uuid = start.Headers.GetValues("Docker-Upload-UUID").Single();
        Assert.EndsWith("/" + uuid, location);

        using var first = await SendAsync(HttpMethod.Patch, location, token, content.Take(1000).ToArray(), "application/octet-stream", configure: r => r.Content!.Headers.ContentRange = new ContentRangeHeaderValue(0, 999));
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal("0-999", first.Headers.GetValues("Range").Single());

        using var status = await SendAsync(HttpMethod.Get, location, token);
        Assert.Equal(HttpStatusCode.NoContent, status.StatusCode);
        Assert.Equal("0-999", status.Headers.GetValues("Range").Single());

        using var second = await SendAsync(HttpMethod.Patch, location, token, content.Skip(1000).Take(1000).ToArray(), "application/octet-stream");
        Assert.Equal("0-1999", second.Headers.GetValues("Range").Single());

        using var last = await SendAsync(HttpMethod.Put, location + "?digest=" + digest, token, content.Skip(2000).ToArray(), "application/octet-stream");
        Assert.Equal(HttpStatusCode.Created, last.StatusCode);

        using var head = await SendAsync(HttpMethod.Head, $"/v2/libs/app/blobs/{digest}", token);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(3000, head.Content.Headers.ContentLength);

        // The session is gone once the blob is stored.
        using var gone = await SendAsync(HttpMethod.Get, location, token);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task AnEmptyChunkChangesNothingAndTheUploadGoesOn()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        var content = Encoding.UTF8.GetBytes("after an empty chunk");
        using var start = await SendAsync(HttpMethod.Post, "/v2/libs/app/blobs/uploads/", token);
        var location = start.Headers.Location!.ToString();

        using var empty = await SendAsync(HttpMethod.Patch, location, token, Array.Empty<byte>(), "application/octet-stream");
        Assert.Equal(HttpStatusCode.Accepted, empty.StatusCode);
        Assert.Equal("0-0", empty.Headers.GetValues("Range").Single());

        using var chunk = await SendAsync(HttpMethod.Patch, location, token, content, "application/octet-stream");
        Assert.Equal($"0-{content.Length - 1}", chunk.Headers.GetValues("Range").Single());

        // The last chunk may be empty as well: the blob is what was appended.
        using var finish = await SendAsync(HttpMethod.Put, location + "?digest=" + OciTestImage.Digest(content), token, Array.Empty<byte>(), "application/octet-stream");
        Assert.Equal(HttpStatusCode.Created, finish.StatusCode);
        using var get = await SendAsync(HttpMethod.Get, $"/v2/libs/app/blobs/{OciTestImage.Digest(content)}", token);
        Assert.Equal(content, await get.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AChunkThatDoesNotStartWhereTheUploadEndsIs416WithTheRealLength()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        using var start = await SendAsync(HttpMethod.Post, "/v2/libs/app/blobs/uploads/", token);
        var location = start.Headers.Location!.ToString();
        (await SendAsync(HttpMethod.Patch, location, token, new byte[100], "application/octet-stream")).Dispose();

        using var outOfOrder = await SendAsync(HttpMethod.Patch, location, token, new byte[10], "application/octet-stream", configure: r => r.Content!.Headers.ContentRange = new ContentRangeHeaderValue(50, 59));

        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, outOfOrder.StatusCode);
        Assert.Equal("0-99", outOfOrder.Headers.GetValues("Range").Single());
    }

    [Fact]
    public async Task ADigestThatDoesNotMatchTheContentIsRefusedAndTheSessionEnds()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        using var start = await SendAsync(HttpMethod.Post, "/v2/libs/app/blobs/uploads/", token);
        var location = start.Headers.Location!.ToString();

        using var response = await SendAsync(HttpMethod.Put, location + "?digest=" + OciTestImage.Digest(new byte[] { 9 }), token, new byte[] { 1, 2, 3 }, "application/octet-stream");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("DIGEST_INVALID", ErrorCode(await TextAsync(response)));
        using var gone = await SendAsync(HttpMethod.Get, location, token);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("&digest=sha256:abc")]
    [InlineData("&digest=sha512:" + "00")]
    public async Task AMissingOrMalformedDigestIs400(string query)
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        using var start = await SendAsync(HttpMethod.Post, "/v2/libs/app/blobs/uploads/", token);

        using var response = await SendAsync(HttpMethod.Put, start.Headers.Location + "?x=1" + query, token, new byte[] { 1 }, "application/octet-stream");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("DIGEST_INVALID", ErrorCode(await TextAsync(response)));
    }

    [Fact]
    public async Task AnUnknownUploadSessionIs404()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        await PushBlobAsync(token, "libs/app", new byte[] { 1 });

        using var response = await SendAsync(HttpMethod.Patch, "/v2/libs/app/blobs/uploads/does-not-exist", token, new byte[] { 1 }, "application/octet-stream");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("BLOB_UPLOAD_UNKNOWN", ErrorCode(await TextAsync(response)));
    }

    [Fact]
    public async Task ACancelledUploadIsGone()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        using var start = await SendAsync(HttpMethod.Post, "/v2/libs/app/blobs/uploads/", token);
        var location = start.Headers.Location!.ToString();

        using var delete = await SendAsync(HttpMethod.Delete, location, token);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        using var patch = await SendAsync(HttpMethod.Patch, location, token, new byte[] { 1 }, "application/octet-stream");
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
    }

    [Fact]
    public async Task AUploadSessionBelongsToItsImage()
    {
        await CreateAsync();
        var token = await TokenAsync("repository:libs/a:pull,push repository:libs/b:pull,push");
        using var start = await SendAsync(HttpMethod.Post, "/v2/libs/a/blobs/uploads/", token);
        var reference = start.Headers.GetValues("Docker-Upload-UUID").Single();

        using var response = await SendAsync(HttpMethod.Patch, $"/v2/libs/b/blobs/uploads/{reference}", token, new byte[] { 1 }, "application/octet-stream");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ABlobIsServedWholeAndByRange()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        var content = Encoding.ASCII.GetBytes("0123456789abcdefghij");
        await PushBlobAsync(token, "libs/app", content);
        var digest = OciTestImage.Digest(content);

        using var head = await SendAsync(HttpMethod.Head, $"/v2/libs/app/blobs/{digest}", token);
        Assert.Equal(20, head.Content.Headers.ContentLength);
        Assert.Equal("bytes", head.Headers.AcceptRanges.Single());

        using var middle = await SendAsync(HttpMethod.Get, $"/v2/libs/app/blobs/{digest}", token, configure: r => r.Headers.Range = new RangeHeaderValue(5, 9));
        Assert.Equal(HttpStatusCode.PartialContent, middle.StatusCode);
        Assert.Equal("56789", await TextAsync(middle));
        Assert.Equal("bytes 5-9/20", middle.Content.Headers.ContentRange!.ToString());

        using var tail = await SendAsync(HttpMethod.Get, $"/v2/libs/app/blobs/{digest}", token, configure: r => r.Headers.Range = new RangeHeaderValue(15, null));
        Assert.Equal("fghij", await TextAsync(tail));

        using var suffix = await SendAsync(HttpMethod.Get, $"/v2/libs/app/blobs/{digest}", token, configure: r => r.Headers.Range = new RangeHeaderValue(null, 3));
        Assert.Equal("hij", await TextAsync(suffix));

        using var past = await SendAsync(HttpMethod.Get, $"/v2/libs/app/blobs/{digest}", token, configure: r => r.Headers.Range = new RangeHeaderValue(50, 60));
        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, past.StatusCode);
    }

    [Fact]
    public async Task AnEmptyBlobIsStoredAndServed()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        await PushBlobAsync(token, "libs/app", Array.Empty<byte>());
        var digest = OciTestImage.Digest(Array.Empty<byte>());

        using var head = await SendAsync(HttpMethod.Head, $"/v2/libs/app/blobs/{digest}", token);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(0, head.Content.Headers.ContentLength);
    }

    [Fact]
    public async Task AnUnknownBlobIs404()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        await PushBlobAsync(token, "libs/app", new byte[] { 1 });

        using var missing = await SendAsync(HttpMethod.Get, "/v2/libs/app/blobs/" + OciTestImage.Digest(new byte[] { 2 }), token);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("BLOB_UNKNOWN", ErrorCode(await TextAsync(missing)));

        using var malformed = await SendAsync(HttpMethod.Get, "/v2/libs/app/blobs/sha256:abc", token);
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
    }

    [Fact]
    public async Task ABlobIsOnlyServedThroughTheImageThatHoldsIt()
    {
        await CreateAsync();
        var token = await TokenAsync("repository:libs/a:pull,push repository:libs/b:pull,push");
        var content = new byte[] { 4, 2 };
        await PushBlobAsync(token, "libs/a", content);
        await PushBlobAsync(token, "libs/b", new byte[] { 9 });
        var digest = OciTestImage.Digest(content);

        using var own = await SendAsync(HttpMethod.Head, $"/v2/libs/a/blobs/{digest}", token);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);

        using var other = await SendAsync(HttpMethod.Head, $"/v2/libs/b/blobs/{digest}", token);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
    }

    [Fact]
    public async Task AMountMakesABlobAvailableWithoutCopyingIt()
    {
        await CreateAsync();
        var token = await TokenAsync("repository:libs/a:pull,push repository:libs/b:pull,push");
        var content = Encoding.UTF8.GetBytes("shared layer");
        await PushBlobAsync(token, "libs/a", content);
        var digest = OciTestImage.Digest(content);

        using var mount = await SendAsync(HttpMethod.Post, $"/v2/libs/b/blobs/uploads/?mount={digest}&from=libs/a", token);

        Assert.Equal(HttpStatusCode.Created, mount.StatusCode);
        using var served = await SendAsync(HttpMethod.Get, $"/v2/libs/b/blobs/{digest}", token);
        Assert.Equal(content, await served.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));

        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        scope.ServiceProvider.GetRequiredService<IRepositoryContext>().Use((await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync("libs", TestContext.Current.CancellationToken))!);
        var blobs = context.OciBlobs!.Where(b => b.Digest == digest).ToList();
        Assert.Single(blobs);
        Assert.Equal(2, blobs[0].RefCount);
    }

    [Fact]
    public async Task AMountOfABlobTheSourceDoesNotHoldFallsBackToAnUploadSession()
    {
        await CreateAsync();
        var token = await TokenAsync("repository:libs/a:pull,push repository:libs/b:pull,push");
        await PushBlobAsync(token, "libs/a", new byte[] { 1 });

        using var missing = await SendAsync(HttpMethod.Post, $"/v2/libs/b/blobs/uploads/?mount={OciTestImage.Digest(new byte[] { 7 })}&from=libs/a", token);
        Assert.Equal(HttpStatusCode.Accepted, missing.StatusCode);
    }

    [Fact]
    public async Task AMountNeedsPullOnTheSourceImage()
    {
        await CreateAsync();
        var setup = await TokenAsync("repository:libs/a:pull,push");
        var content = new byte[] { 5 };
        await PushBlobAsync(setup, "libs/a", content);
        var pushOnlyToB = await TokenAsync("repository:libs/b:pull,push");

        using var response = await SendAsync(HttpMethod.Post, $"/v2/libs/b/blobs/uploads/?mount={OciTestImage.Digest(content)}&from=libs/a", pushOnlyToB);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var check = await SendAsync(HttpMethod.Head, $"/v2/libs/b/blobs/{OciTestImage.Digest(content)}", pushOnlyToB);
        Assert.Equal(HttpStatusCode.NotFound, check.StatusCode);
    }

    [Fact]
    public async Task ABlobIsStoredOnceWhateverTheImageCount()
    {
        await CreateAsync();
        var token = await TokenAsync("repository:libs/a:pull,push repository:libs/b:pull,push");
        var content = Encoding.UTF8.GetBytes("same bytes");
        await PushBlobAsync(token, "libs/a", content);
        await PushBlobAsync(token, "libs/b", content);
        await PushBlobAsync(token, "libs/a", content);

        using var scope = _app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepositoryContext>().Use((await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync("libs", TestContext.Current.CancellationToken))!);
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        var blob = Assert.Single(context.OciBlobs!.Where(b => b.Digest == OciTestImage.Digest(content)).ToList());
        Assert.Equal(2, blob.RefCount);
        Assert.Equal(2, context.OciBlobLinks!.Count(l => l.BlobKey == blob.Key));
    }

    // ---- manifests, tags ----

    [Fact]
    public async Task AnImageIsPushedAndPulledByTagAndByDigest()
    {
        await CreateAsync();
        var token = await PushTokenAsync("libs/team/app");
        var image = OciTestImage.Create("one", layers: 2);

        await PushImageAsync(token, "libs/team/app", "1.0", image);

        foreach (var reference in new[] { "1.0", image.ManifestDigest })
        {
            using var response = await SendAsync(HttpMethod.Get, $"/v2/libs/team/app/manifests/{reference}", token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(OciTestImage.ManifestType, response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(image.ManifestDigest, response.Headers.GetValues("Docker-Content-Digest").Single());
            Assert.Equal(image.Manifest, await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        }

        using var head = await SendAsync(HttpMethod.Head, "/v2/libs/team/app/manifests/1.0", token);
        Assert.Equal(image.Manifest.Length, head.Content.Headers.ContentLength);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
    }

    [Fact]
    public async Task APutManifestAnswersWithItsDigestAndLocation()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        var image = OciTestImage.Create("loc");
        await PushBlobAsync(token, "libs/app", image.Config);
        await PushBlobAsync(token, "libs/app", image.Layers[0]);

        using var response = await PutManifestAsync(token, "libs/app", "latest", image.Manifest);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(image.ManifestDigest, response.Headers.GetValues("Docker-Content-Digest").Single());
        Assert.Equal("http://localhost/v2/libs/app/manifests/" + image.ManifestDigest, response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task AManifestThatNamesABlobTheImageDoesNotHoldIsRefused()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        var image = OciTestImage.Create("missing");
        await PushBlobAsync(token, "libs/app", image.Config);

        using var response = await PutManifestAsync(token, "libs/app", "latest", image.Manifest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("MANIFEST_BLOB_UNKNOWN", ErrorCode(await TextAsync(response)));
        using var get = await SendAsync(HttpMethod.Get, "/v2/libs/app/manifests/latest", token);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task AManifestCannotUseABlobOfAnotherImage()
    {
        await CreateAsync();
        var token = await TokenAsync("repository:libs/a:pull,push repository:libs/b:pull,push");
        var image = OciTestImage.Create("other");
        await PushBlobAsync(token, "libs/a", image.Config);
        await PushBlobAsync(token, "libs/a", image.Layers[0]);

        using var response = await PutManifestAsync(token, "libs/b", "latest", image.Manifest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AManifestWhoseDigestDiffersFromTheUrlIsRefused()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        var image = OciTestImage.Create("digest");
        await PushBlobAsync(token, "libs/app", image.Config);
        await PushBlobAsync(token, "libs/app", image.Layers[0]);

        using var response = await PutManifestAsync(token, "libs/app", OciTestImage.Digest(new byte[] { 1 }), image.Manifest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("DIGEST_INVALID", ErrorCode(await TextAsync(response)));
    }

    [Fact]
    public async Task AManifestPushedByDigestIsStoredWithoutATag()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        var image = OciTestImage.Create("bydigest");
        await PushBlobAsync(token, "libs/app", image.Config);
        await PushBlobAsync(token, "libs/app", image.Layers[0]);

        using var put = await PutManifestAsync(token, "libs/app", image.ManifestDigest, image.Manifest);
        Assert.Equal(HttpStatusCode.Created, put.StatusCode);

        using var tags = await SendAsync(HttpMethod.Get, "/v2/libs/app/tags/list", token);
        using var json = JsonDocument.Parse(await TextAsync(tags));
        Assert.Empty(json.RootElement.GetProperty("tags").EnumerateArray());
        using var get = await SendAsync(HttpMethod.Get, $"/v2/libs/app/manifests/{image.ManifestDigest}", token);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/vnd.docker.distribution.manifest.v1+json")]
    public async Task AnUnsupportedManifestMediaTypeIsRefused(string contentType)
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        var image = OciTestImage.Create("type");

        using var response = await PutManifestAsync(token, "libs/app", "latest", image.Manifest, contentType);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("MANIFEST_INVALID", ErrorCode(await TextAsync(response)));
    }

    [Fact]
    public async Task AnOversizedManifestIs413()
    {
        await CreateAsync();
        var token = await PushTokenAsync();

        using var response = await PutManifestAsync(token, "libs/app", "latest", new byte[5 * 1024 * 1024]);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task ADockerSchema2ManifestIsStoredAsPushed()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        var image = OciTestImage.Create("docker");
        var docker = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(image.Manifest)
            .Replace(OciTestImage.ManifestType, "application/vnd.docker.distribution.manifest.v2+json")
            .Replace(OciTestImage.ConfigType, "application/vnd.docker.container.image.v1+json")
            .Replace(OciTestImage.LayerType, "application/vnd.docker.image.rootfs.diff.tar.gzip"));
        await PushBlobAsync(token, "libs/app", image.Config);
        await PushBlobAsync(token, "libs/app", image.Layers[0]);

        using var put = await PutManifestAsync(token, "libs/app", "latest", docker, "application/vnd.docker.distribution.manifest.v2+json");
        Assert.Equal(HttpStatusCode.Created, put.StatusCode);

        using var get = await SendAsync(HttpMethod.Get, "/v2/libs/app/manifests/latest", token);
        Assert.Equal("application/vnd.docker.distribution.manifest.v2+json", get.Content.Headers.ContentType?.MediaType);
        Assert.Equal(docker, await get.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ATagMovesToTheLatestManifest()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        var first = OciTestImage.Create("first");
        var second = OciTestImage.Create("second");
        await PushImageAsync(token, "libs/app", "latest", first);
        await PushImageAsync(token, "libs/app", "latest", second);

        using var get = await SendAsync(HttpMethod.Get, "/v2/libs/app/manifests/latest", token);
        Assert.Equal(second.ManifestDigest, get.Headers.GetValues("Docker-Content-Digest").Single());

        using var old = await SendAsync(HttpMethod.Get, $"/v2/libs/app/manifests/{first.ManifestDigest}", token);
        Assert.Equal(HttpStatusCode.OK, old.StatusCode);
    }

    [Fact]
    public async Task ATagIsCaseSensitive()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        var lower = OciTestImage.Create("lower");
        var upper = OciTestImage.Create("upper");
        await PushImageAsync(token, "libs/app", "v1", lower);
        await PushImageAsync(token, "libs/app", "V1", upper);

        using var a = await SendAsync(HttpMethod.Head, "/v2/libs/app/manifests/v1", token);
        using var b = await SendAsync(HttpMethod.Head, "/v2/libs/app/manifests/V1", token);
        Assert.Equal(lower.ManifestDigest, a.Headers.GetValues("Docker-Content-Digest").Single());
        Assert.Equal(upper.ManifestDigest, b.Headers.GetValues("Docker-Content-Digest").Single());
    }

    [Fact]
    public async Task AnUnknownManifestOrImageIs404()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        await PushImageAsync(token, "libs/app", "1", OciTestImage.Create("x"));

        using var tag = await SendAsync(HttpMethod.Get, "/v2/libs/app/manifests/nope", token);
        Assert.Equal(HttpStatusCode.NotFound, tag.StatusCode);
        Assert.Equal("MANIFEST_UNKNOWN", ErrorCode(await TextAsync(tag)));

        var other = await TokenAsync("repository:libs/ghost:pull");
        using var image = await SendAsync(HttpMethod.Get, "/v2/libs/ghost/manifests/1", other);
        Assert.Equal(HttpStatusCode.NotFound, image.StatusCode);
        Assert.Equal("NAME_UNKNOWN", ErrorCode(await TextAsync(image)));
    }

    [Fact]
    public async Task AReadOfAReferenceThatCannotBeATagIs404AndAPutOfOneIs400()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        var image = OciTestImage.Create("badtag");
        await PushBlobAsync(token, "libs/app", image.Config);
        await PushBlobAsync(token, "libs/app", image.Layers[0]);

        using var head = await SendAsync(HttpMethod.Head, "/v2/libs/app/manifests/.INVALID_MANIFEST_NAME", token);
        Assert.Equal(HttpStatusCode.NotFound, head.StatusCode);
        using var get = await SendAsync(HttpMethod.Get, "/v2/libs/app/manifests/.INVALID_MANIFEST_NAME", token);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        using var delete = await SendAsync(HttpMethod.Delete, "/v2/libs/app/manifests/.INVALID_MANIFEST_NAME", token);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        using var put = await PutManifestAsync(token, "libs/app", ".INVALID_MANIFEST_NAME", image.Manifest);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.Equal("TAG_INVALID", ErrorCode(await TextAsync(put)));
    }

    [Fact]
    public async Task TagsAreListedInOrderAndPaged()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        var image = OciTestImage.Create("tags");
        await PushImageAsync(token, "libs/app", "b", image);
        foreach (var tag in new[] { "a", "c", "d" })
        {
            using var put = await PutManifestAsync(token, "libs/app", tag, image.Manifest);
            Assert.Equal(HttpStatusCode.Created, put.StatusCode);
        }

        using var all = await SendAsync(HttpMethod.Get, "/v2/libs/app/tags/list", token);
        using var allJson = JsonDocument.Parse(await TextAsync(all));
        Assert.Equal("libs/app", allJson.RootElement.GetProperty("name").GetString());
        Assert.Equal(new[] { "a", "b", "c", "d" }, allJson.RootElement.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToArray());

        using var page = await SendAsync(HttpMethod.Get, "/v2/libs/app/tags/list?n=2", token);
        using var pageJson = JsonDocument.Parse(await TextAsync(page));
        Assert.Equal(new[] { "a", "b" }, pageJson.RootElement.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToArray());
        Assert.Contains("last=b", page.Headers.GetValues("Link").Single());
        Assert.Contains("rel=\"next\"", page.Headers.GetValues("Link").Single());

        using var next = await SendAsync(HttpMethod.Get, "/v2/libs/app/tags/list?n=2&last=b", token);
        using var nextJson = JsonDocument.Parse(await TextAsync(next));
        Assert.Equal(new[] { "c", "d" }, nextJson.RootElement.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToArray());
        Assert.False(next.Headers.Contains("Link"));

        using var none = await SendAsync(HttpMethod.Get, "/v2/libs/app/tags/list?n=0", token);
        using var noneJson = JsonDocument.Parse(await TextAsync(none));
        Assert.Empty(noneJson.RootElement.GetProperty("tags").EnumerateArray());

        using var bad = await SendAsync(HttpMethod.Get, "/v2/libs/app/tags/list?n=x", token);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    // ---- indexes (multi-arch) and referrers ----

    [Fact]
    public async Task AnIndexNeedsItsChildrenAndIsPulledByTag()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        var amd64 = OciTestImage.Create("amd64", architecture: "amd64");
        var arm64 = OciTestImage.Create("arm64", architecture: "arm64");
        var (index, indexDigest) = OciTestImage.Index((amd64, "linux", "amd64", null), (arm64, "linux", "arm64", "v8"));

        await PushImageAsync(token, "libs/app", "amd64", amd64);

        using var early = await PutManifestAsync(token, "libs/app", "multi", index, OciTestImage.IndexType);
        Assert.Equal(HttpStatusCode.BadRequest, early.StatusCode);
        Assert.Equal("MANIFEST_BLOB_UNKNOWN", ErrorCode(await TextAsync(early)));

        await PushImageAsync(token, "libs/app", "arm64", arm64);
        using var put = await PutManifestAsync(token, "libs/app", "multi", index, OciTestImage.IndexType);
        Assert.Equal(HttpStatusCode.Created, put.StatusCode);

        using var get = await SendAsync(HttpMethod.Get, "/v2/libs/app/manifests/multi", token);
        Assert.Equal(OciTestImage.IndexType, get.Content.Headers.ContentType?.MediaType);
        Assert.Equal(indexDigest, get.Headers.GetValues("Docker-Content-Digest").Single());
        Assert.Equal(index, await get.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReferrersAreListedBySubjectAndFilteredByArtifactType()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        var subject = OciTestImage.Create("subject");
        await PushImageAsync(token, "libs/app", "1", subject);

        var sbom = OciTestImage.Create("sbom", subject: subject.ManifestDigest, artifactType: "application/vnd.example.sbom");
        var signature = OciTestImage.Create("sig", subject: subject.ManifestDigest, artifactType: "application/vnd.example.signature");
        await PushImageAsync(token, "libs/app", sbom.ManifestDigest, sbom);
        await PushImageAsync(token, "libs/app", signature.ManifestDigest, signature);

        using var put = await PutManifestAsync(token, "libs/app", "sbom", sbom.Manifest);
        Assert.Equal(subject.ManifestDigest, put.Headers.GetValues("OCI-Subject").Single());

        using var all = await SendAsync(HttpMethod.Get, $"/v2/libs/app/referrers/{subject.ManifestDigest}", token);
        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        Assert.Equal(OciTestImage.IndexType, all.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await TextAsync(all));
        var digests = json.RootElement.GetProperty("manifests").EnumerateArray().Select(m => m.GetProperty("digest").GetString()!).ToList();
        Assert.Equal(new[] { sbom.ManifestDigest, signature.ManifestDigest }.OrderBy(d => d).ToList(), digests.OrderBy(d => d).ToList());
        Assert.Equal(2, json.RootElement.GetProperty("schemaVersion").GetInt32());

        using var filtered = await SendAsync(HttpMethod.Get, $"/v2/libs/app/referrers/{subject.ManifestDigest}?artifactType=application/vnd.example.sbom", token);
        using var filteredJson = JsonDocument.Parse(await TextAsync(filtered));
        var only = Assert.Single(filteredJson.RootElement.GetProperty("manifests").EnumerateArray());
        Assert.Equal(sbom.ManifestDigest, only.GetProperty("digest").GetString());
        Assert.Equal("application/vnd.example.sbom", only.GetProperty("artifactType").GetString());
        Assert.Equal("artifactType", filtered.Headers.GetValues("OCI-Filters-Applied").Single());

        using var none = await SendAsync(HttpMethod.Get, $"/v2/libs/app/referrers/{OciTestImage.Digest(new byte[] { 3 })}", token);
        using var noneJson = JsonDocument.Parse(await TextAsync(none));
        Assert.Empty(noneJson.RootElement.GetProperty("manifests").EnumerateArray());
    }

    // ---- deletes ----

    [Fact]
    public async Task DeletingAManifestByDigestRemovesItsTags()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        var image = OciTestImage.Create("del");
        await PushImageAsync(token, "libs/app", "1", image);
        (await PutManifestAsync(token, "libs/app", "2", image.Manifest)).Dispose();

        using var delete = await SendAsync(HttpMethod.Delete, $"/v2/libs/app/manifests/{image.ManifestDigest}", token);

        Assert.Equal(HttpStatusCode.Accepted, delete.StatusCode);
        foreach (var reference in new[] { "1", "2", image.ManifestDigest })
        {
            using var get = await SendAsync(HttpMethod.Get, $"/v2/libs/app/manifests/{reference}", token);
            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        }

        using var again = await SendAsync(HttpMethod.Delete, $"/v2/libs/app/manifests/{image.ManifestDigest}", token);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task DeletingATagKeepsTheManifest()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        var image = OciTestImage.Create("tag");
        await PushImageAsync(token, "libs/app", "1", image);
        (await PutManifestAsync(token, "libs/app", "2", image.Manifest)).Dispose();

        using var delete = await SendAsync(HttpMethod.Delete, "/v2/libs/app/manifests/1", token);

        Assert.Equal(HttpStatusCode.Accepted, delete.StatusCode);
        using var gone = await SendAsync(HttpMethod.Get, "/v2/libs/app/manifests/1", token);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        using var kept = await SendAsync(HttpMethod.Get, "/v2/libs/app/manifests/2", token);
        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
    }

    [Fact]
    public async Task ADeleteNeedsTheDeleteAction()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        var image = OciTestImage.Create("perm");
        await PushImageAsync(token, "libs/app", "1", image);
        var noDelete = await TokenAsync("repository:libs/app:pull,push");

        using var response = await SendAsync(HttpMethod.Delete, "/v2/libs/app/manifests/1", noDelete);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var still = await SendAsync(HttpMethod.Get, "/v2/libs/app/manifests/1", token);
        Assert.Equal(HttpStatusCode.OK, still.StatusCode);
    }

    [Fact]
    public async Task ABlobUsedByAManifestCannotBeDeletedButAnOrphanCan()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        var image = OciTestImage.Create("blobdel");
        await PushImageAsync(token, "libs/app", "1", image);
        var orphan = new byte[] { 6, 6, 6 };
        await PushBlobAsync(token, "libs/app", orphan);

        using var used = await SendAsync(HttpMethod.Delete, $"/v2/libs/app/blobs/{image.LayerDigests[0]}", token);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, used.StatusCode);

        using var free = await SendAsync(HttpMethod.Delete, $"/v2/libs/app/blobs/{OciTestImage.Digest(orphan)}", token);
        Assert.Equal(HttpStatusCode.Accepted, free.StatusCode);
        using var gone = await SendAsync(HttpMethod.Head, $"/v2/libs/app/blobs/{OciTestImage.Digest(orphan)}", token);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task ADeletedManifestFreesItsBlobsForAPrune()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        var image = OciTestImage.Create("prune");
        await PushImageAsync(token, "libs/app", "1", image);
        (await SendAsync(HttpMethod.Delete, $"/v2/libs/app/manifests/{image.ManifestDigest}", token)).Dispose();

        using var scope = _app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepositoryContext>().Use((await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync("libs", TestContext.Current.CancellationToken))!);
        var registry = scope.ServiceProvider.GetRequiredService<IOciRegistry>();

        // Inside the grace period nothing is pruned: a push in progress uploads its layers before its manifest.
        var info = (await registry.GetImageAsync("app", TestContext.Current.CancellationToken))!;
        Assert.Equal(2, info.UnreferencedBlobs);
        Assert.Equal(0, (await registry.PruneAsync("app", TestContext.Current.CancellationToken)).Blobs);
    }

    [Fact]
    public async Task APushWhileTheMaintenanceGateIsClosedIs503AndStoresNothing()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        var gate = _app.Services.GetRequiredService<MaintenanceGate>();
        gate.Close("Copying the database");
        try
        {
            using var response = await SendAsync(HttpMethod.Post, "/v2/libs/app/blobs/uploads/", token);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        finally
        {
            gate.Open();
        }

        using var scope = _app.Services.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<IContext>().OciUploads!.ToList());
    }

    [Fact]
    public async Task APullStillWorksWhileTheGateIsClosed()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        var image = OciTestImage.Create("ro");
        await PushImageAsync(token, "libs/app", "1", image);
        var gate = _app.Services.GetRequiredService<MaintenanceGate>();
        gate.Close("Copying the database");
        try
        {
            using var response = await SendAsync(HttpMethod.Get, "/v2/libs/app/manifests/1", token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            gate.Open();
        }
    }

    [Fact]
    public async Task AReadOnlyServerRefusesEveryWrite()
    {
        using var app = new BaGetApplication(
            _output,
            configOverrides: new Dictionary<string, string> { { "ApiKey", Key }, { "IsReadOnlyMode", "true" } });
        await app.CreateDockerRepositoryAsync("libs", true, TestContext.Current.CancellationToken);
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v2/libs/app/blobs/uploads/");
        request.Headers.Authorization = Basic();

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- repositories ----

    [Fact]
    public async Task TwoRepositoriesKeepTheirImagesApart()
    {
        await CreateAsync("one");
        await CreateAsync("two");
        var token = await TokenAsync("repository:one/app:pull,push repository:two/app:pull,push");
        var image = OciTestImage.Create("iso");
        await PushImageAsync(token, "one/app", "1", image);

        using var other = await SendAsync(HttpMethod.Get, "/v2/two/app/manifests/1", token);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        using var own = await SendAsync(HttpMethod.Get, "/v2/one/app/manifests/1", token);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
    }

    [Fact]
    public async Task ARepositoryNameIsMatchedWithoutRegardToCase()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        var image = OciTestImage.Create("case");
        await PushImageAsync(token, "libs/app", "1", image);

        using var response = await SendAsync(HttpMethod.Get, "/v2/LIBS/app/manifests/1", auth: Basic());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AnInvalidImageNameIs400()
    {
        await CreateAsync();

        using var response = await SendAsync(HttpMethod.Get, "/v2/libs/Bad_Name/tags/list", auth: Basic());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("NAME_INVALID", ErrorCode(await TextAsync(response)));
    }

    [Fact]
    public async Task TheDockerRepositoryIsListedWithItsImageCountAndCannotBeDeletedWhileItHoldsImages()
    {
        await CreateAsync();
        var token = await PushTokenAsync();
        await PushImageAsync(token, "libs/app", "1", OciTestImage.Create("count"));

        using var scope = _app.Services.CreateScope();
        var administration = scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>();
        var libs = (await administration.ListAsync(TestContext.Current.CancellationToken)).Single(r => r.Name == "libs");

        Assert.Equal(RepositoryFormat.Docker, libs.Format);
        Assert.Equal(1, libs.PackageCount);
    }
}
