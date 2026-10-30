using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// The JSON twins (<c>/api/ui</c>) of the NuGet upload screen and of the npm, Maven and Docker screens (plan, wave 1, <c>split-formats</c>): the model the twin
/// carries is what the page shows, the statuses correspond (404 to 404, sign-in redirect to 401), the uploads work with an API key (the cookie is not a credential on the twins, so a browser
/// session is anonymous there and there is no antiforgery), a refusal carries the status of the refusal, and nothing a request carried (an API key) comes back in a response.
/// </summary>
public class UiApiFormatScreensTests : IDisposable
{
    private const string PastedKey = "pasted-key-0123456789";

    private readonly BaGetApplication _app;
    private readonly Browser _anonymous;
    private readonly Browser _alice;
    private string _aliceKey = string.Empty;

    public UiApiFormatScreensTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "true" }, { "ApiKey", "" } });
        _anonymous = new Browser(_app);
        _alice = new Browser(_app);
        SetUpAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _anonymous.Dispose();
        _alice.Dispose();
        _app.Dispose();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task SetUpAsync()
    {
        await _app.CreateNpmRepositoryAsync("web", cancellationToken: CancellationToken.None);
        await _app.CreateMavenRepositoryAsync("libs", policy: "Both", cancellationToken: CancellationToken.None);
        await _app.CreateDockerRepositoryAsync("reg", cancellationToken: CancellationToken.None);
        await CreateUserAsync(_app, "alice");
        await SignInAsync(_alice, "alice");

        using var scope = _app.Services.CreateScope();
        _aliceKey = await scope.ServiceProvider.GetRequiredService<ApiKeyService>()
            .CreateAsync((await FindAsync(_app, "alice"))!.Id, "t", ApiKeyScopes.All, "*", null, CancellationToken.None);
    }

    private sealed record Answer(HttpStatusCode Status, JsonNode? Json, string Body);

    private static async Task<Answer> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Token);

        return new Answer(response.StatusCode, body.Length > 0 && body[0] == '{' ? JsonNode.Parse(body) : null, body);
    }

    private async Task<Answer> GetAsync(Browser browser, string path, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (key != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await browser.SendAsync(request);

        return await ReadAsync(response);
    }

    private async Task<Answer> PostAsync(Browser browser, string path, HttpContent content, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        if (key != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await browser.SendAsync(request);

        return await ReadAsync(response);
    }

    private static MultipartFormDataContent Multipart(string fileField, string? fileName, byte[]? file, Dictionary<string, string>? fields = null)
    {
        var content = new MultipartFormDataContent();
        foreach (var pair in fields ?? new Dictionary<string, string>()) content.Add(new StringContent(pair.Value), pair.Key);
        if (file != null)
        {
            var part = new ByteArrayContent(file);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(part, fileField, fileName!);
        }

        return content;
    }

    private static byte[] NuGetPackage()
    {
        using var stream = TestResources.GetResourceStream(TestResources.Package)!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);

        return copy.ToArray();
    }

    // ---- NuGet upload ----

    [Fact]
    public async Task TheUploadFormIsTheSameStateOnThePageAndOnItsTwin()
    {
        var page = await GetAsync(_alice, "/upload");
        var api = await GetAsync(_anonymous, "/api/ui/upload", _aliceKey);
        var anonymousApi = await GetAsync(_anonymous, "/api/ui/upload");

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(HttpStatusCode.OK, api.Status);
        Assert.Equal("Ok", api.Json!["status"]!.GetValue<string>());
        Assert.True(api.Json["model"]!["canUpload"]!.GetValue<bool>());
        Assert.True(api.Json["model"]!["signedIn"]!.GetValue<bool>());
        Assert.Contains("name=\"package\"", page.Body);

        // An anonymous visitor has no form to use: the same state in each model.
        Assert.False(anonymousApi.Json!["model"]!["canUpload"]!.GetValue<bool>());
    }

    [Fact]
    public async Task AKeyHolderStagesPublishesAndDiscardsThroughTheTwinWithoutAnyAntiforgeryHeader()
    {
        var staged = await PostAsync(_anonymous, "/api/ui/upload/stage", Multipart("package", "TestData.1.2.3.nupkg", NuGetPackage()), key: _aliceKey);

        Assert.Equal(HttpStatusCode.OK, staged.Status);
        var preview = staged.Json!["model"]!["staged"]!;
        Assert.Equal("TestData", preview["id"]!.GetValue<string>());
        Assert.Equal("1.2.3", preview["version"]!.GetValue<string>());
        var token = preview["token"]!.GetValue<string>();

        // Without the terms accepted the twin refuses with 400 and keeps the upload staged.
        var refused = await PostAsync(_anonymous, "/api/ui/upload/submit", Json($"{{\"token\":\"{token}\"}}"), key: _aliceKey);
        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.Equal("Invalid", refused.Json!["status"]!.GetValue<string>());
        Assert.NotEmpty(refused.Json["errors"]![""]!.AsArray());

        var published = await PostAsync(_anonymous, "/api/ui/upload/submit", Json($"{{\"token\":\"{token}\",\"acceptTerms\":true}}"), key: _aliceKey);
        Assert.Equal(HttpStatusCode.OK, published.Status);
        Assert.Equal("TestData", published.Json!["model"]!["published"]!["id"]!.GetValue<string>());

        using var download = await _alice.GetAsync("v3/package/testdata/1.2.3/testdata.1.2.3.nupkg");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);

        // The token names nothing now: the same sentence as the page, with its status.
        var again = await PostAsync(_anonymous, "/api/ui/upload/cancel", Json($"{{\"token\":\"{token}\"}}"), key: _aliceKey);
        Assert.Equal(HttpStatusCode.NotFound, again.Status);
        Assert.Contains("no longer staged", again.Json!["errors"]![""]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task ACookieIsNotACredentialOnTheUploadTwinAndAnApiKeyIs()
    {
        // The signed-in browser is anonymous on /api/ui: refused like a visitor who did not sign in.
        var cookie = await PostAsync(_alice, "/api/ui/upload/stage", Multipart("package", "TestData.1.2.3.nupkg", NuGetPackage()));
        Assert.Equal(HttpStatusCode.Unauthorized, cookie.Status);
        Assert.Equal("Sign in to upload a package.", cookie.Json!["errors"]![""]![0]!.GetValue<string>());

        // The key's owner is the uploader.
        var staged = await PostAsync(_anonymous, "/api/ui/upload/stage", Multipart("package", "TestData.1.2.3.nupkg", NuGetPackage()), key: _aliceKey);
        Assert.Equal(HttpStatusCode.OK, staged.Status);
        Assert.Equal("TestData", staged.Json!["model"]!["staged"]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task AnAnonymousUploadIsRefusedWith401AndTheSentenceOfThePage()
    {
        using var content = Multipart("package", "TestData.1.2.3.nupkg", NuGetPackage());
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ui/upload/stage") { Content = content };
        using var response = await _anonymous.SendAsync(request);
        var api = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.Unauthorized, api.Status);
        Assert.Equal("Invalid", api.Json!["status"]!.GetValue<string>());
        Assert.Equal("Sign in to upload a package.", api.Json["errors"]![""]![0]!.GetValue<string>());
        Assert.Equal(401, api.Json["model"]!["statusCode"]!.GetValue<int>());
        Assert.NotNull(response.Headers.WwwAuthenticate);
    }

    [Fact]
    public async Task AnUnreadableFileAndAMissingFileAreRefusedWith400()
    {
        var notAPackage = await PostAsync(_anonymous, "/api/ui/upload/stage", Multipart("package", "x.nupkg", Encoding.UTF8.GetBytes("not a zip")), key: _aliceKey);
        Assert.Equal(HttpStatusCode.BadRequest, notAPackage.Status);
        Assert.Contains("could not be read as a package", notAPackage.Json!["errors"]![""]![0]!.GetValue<string>());

        var noFile = await PostAsync(_anonymous, "/api/ui/upload/stage", Multipart("package", null, null), key: _aliceKey);
        Assert.Equal(HttpStatusCode.BadRequest, noFile.Status);
        Assert.Contains("Choose a .nupkg", noFile.Json!["errors"]![""]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task AnApiKeyOfTheRequestIsNeverEchoedInTheAnswer()
    {
        var fields = new Dictionary<string, string> { { "apiKey", PastedKey } };
        var answer = await PostAsync(_anonymous, "/api/ui/upload/stage", Multipart("package", "x.nupkg", Encoding.UTF8.GetBytes("not a zip"), fields), key: _aliceKey);

        Assert.DoesNotContain(PastedKey, answer.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(_aliceKey, answer.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheUploadTwinOfAnotherRepositoryFollowsTheRepositoryRule()
    {
        await _app.CreateRepositoryAsync("second", Token);

        var known = await GetAsync(_anonymous, "/api/ui/r/second/upload", _aliceKey);
        var unknown = await GetAsync(_anonymous, "/api/ui/r/nope/upload", _aliceKey);
        var unknownPage = await GetAsync(_alice, "/r/nope/upload");

        Assert.Equal(HttpStatusCode.OK, known.Status);
        Assert.Equal(HttpStatusCode.NotFound, unknown.Status);
        Assert.Equal(HttpStatusCode.NotFound, unknownPage.Status);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    // ---- npm ----

    private async Task PublishNpmAsync(string name, string version)
    {
        using var client = _app.CreateClient();
        using var request = NpmTestPackages.Json(
            HttpMethod.Put,
            NpmTestPackages.Path("/r/web/npm/", name),
            NpmTestPackages.PublishBody(NpmTestPackages.Tarball(name, version, readme: "# Hello readme"), "latest", "# Hello readme"),
            _aliceKey);
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task TheNpmListAndPackageTwinsCarryWhatThePagesShow()
    {
        await PublishNpmAsync("left-pad", "1.0.0");

        var listPage = await GetAsync(_anonymous, "/r/web/npm-packages?q=left");
        var list = await GetAsync(_anonymous, "/api/ui/r/web/npm-packages?q=left");
        var packagePage = await GetAsync(_anonymous, "/r/web/npm-packages/left-pad");
        var package = await GetAsync(_anonymous, "/api/ui/r/web/npm-packages/left-pad");

        Assert.Equal(HttpStatusCode.OK, list.Status);
        Assert.Equal(1, list.Json!["model"]!["result"]!["total"]!.GetValue<int>());
        Assert.Equal("left-pad", list.Json["model"]!["result"]!["hits"]![0]!["name"]!.GetValue<string>());
        Assert.Contains("left-pad", listPage.Body);

        Assert.Equal(HttpStatusCode.OK, package.Status);
        var model = package.Json!["model"]!;
        Assert.Equal("left-pad", model["package"]!["name"]!.GetValue<string>());
        Assert.Equal("1.0.0", model["selected"]!["version"]!.GetValue<string>());
        // The readme is rendered HTML as text, the install commands are data: a client needs no templating.
        Assert.Contains("<h1", model["readme"]!.GetValue<string>());
        Assert.NotEmpty(model["installTabs"]!.AsArray());
        Assert.Contains("npm install", package.Body);
        Assert.Contains("Hello readme", packagePage.Body);
    }

    [Fact]
    public async Task AMissingNpmPackageIs404WithTheExplainingScreenAndAnInvalidNameIsNotFound()
    {
        var page = await GetAsync(_anonymous, "/r/web/npm-packages/nobody");
        var api = await GetAsync(_anonymous, "/api/ui/r/web/npm-packages/nobody");
        var invalidPage = await GetAsync(_anonymous, "/r/web/npm-packages/BAD NAME");
        var invalid = await GetAsync(_anonymous, "/api/ui/r/web/npm-packages/BAD NAME");

        Assert.Equal(HttpStatusCode.NotFound, page.Status);
        Assert.Equal(HttpStatusCode.NotFound, api.Status);
        Assert.Equal(404, api.Json!["model"]!["statusCode"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.NotFound, invalidPage.Status);
        Assert.Equal(HttpStatusCode.NotFound, invalid.Status);
        Assert.Equal("NotFound", invalid.Json!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task TheNpmScreensAreNotInARepositoryOfAnotherFormat()
    {
        // The default repository is NuGet's.
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(_anonymous, "/npm-packages")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(_anonymous, "/api/ui/npm-packages")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(_anonymous, "/api/ui/r/libs/npm-packages")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(_anonymous, "/api/ui/r/web/maven-artifacts")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(_anonymous, "/api/ui/r/web/docker-images")).Status);
    }

    [Fact]
    public async Task APageNumberBelowOneIs400OnTheTwin()
    {
        var api = await GetAsync(_anonymous, "/api/ui/r/web/npm-packages?p=0");

        Assert.Equal(HttpStatusCode.BadRequest, api.Status);
        Assert.Equal("Invalid", api.Json!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task AnNpmTarballIsPublishedThroughTheTwinWithAKeyAndRefusedWithoutOne()
    {
        var tarball = NpmTestPackages.Tarball("up-lib", "2.0.0");

        var published = await PostAsync(_anonymous, "/api/ui/r/web/npm-upload", Multipart("package", "up-lib-2.0.0.tgz", tarball.Bytes, new Dictionary<string, string> { { "tag", "next" } }), key: _aliceKey);
        Assert.Equal(HttpStatusCode.OK, published.Status);
        Assert.Equal("up-lib", published.Json!["model"]!["publishedName"]!.GetValue<string>());

        var package = await GetAsync(_anonymous, "/api/ui/r/web/npm-packages/up-lib");
        Assert.Equal("up-lib", package.Json!["model"]!["package"]!["name"]!.GetValue<string>());

        using var content = Multipart("package", "up-lib-2.0.1.tgz", NpmTestPackages.Tarball("up-lib", "2.0.1").Bytes);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ui/r/web/npm-upload") { Content = content };
        using var response = await _anonymous.SendAsync(request);
        var refused = await ReadAsync(response);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.Status);
        Assert.Equal("Sign in to upload a package.", refused.Json!["errors"]![""]![0]!.GetValue<string>());
    }

    // ---- Maven ----

    private async Task PutMavenAsync(string path, string content)
    {
        using var client = _app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/r/libs/maven/{path}") { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(content)) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:" + _aliceKey)));
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task TheMavenTwinsCarryWhatThePagesShowAndAMissingArtifactIs404()
    {
        var path = "org/acme/lib/1.0/lib-1.0";
        await PutMavenAsync(path + ".jar", "jar bytes");
        await PutMavenAsync(path + ".pom", MavenPom.Generate("org.acme", "lib", "1.0", "jar", "The lib", "About lib"));

        var listPage = await GetAsync(_anonymous, "/r/libs/maven-artifacts");
        var list = await GetAsync(_anonymous, "/api/ui/r/libs/maven-artifacts");
        var artifactPage = await GetAsync(_anonymous, "/r/libs/maven-artifacts/org.acme/lib/1.0");
        var artifact = await GetAsync(_anonymous, "/api/ui/r/libs/maven-artifacts/org.acme/lib/1.0");
        var missingPage = await GetAsync(_anonymous, "/r/libs/maven-artifacts/org.acme/nope");
        var missing = await GetAsync(_anonymous, "/api/ui/r/libs/maven-artifacts/org.acme/nope");

        Assert.Equal(1, list.Json!["model"]!["result"]!["total"]!.GetValue<int>());
        Assert.NotEmpty(list.Json["model"]!["configurationTabs"]!.AsArray());
        Assert.Contains("lib", listPage.Body);

        Assert.Equal(HttpStatusCode.OK, artifact.Status);
        Assert.Equal("org.acme", artifact.Json!["model"]!["info"]!["groupId"]!.GetValue<string>());
        Assert.Equal("1.0", artifact.Json["model"]!["selected"]!["summary"]!["version"]!.GetValue<string>());
        Assert.NotEmpty(artifact.Json["model"]!["installTabs"]!.AsArray());
        Assert.Contains("org.acme", artifactPage.Body);

        Assert.Equal(HttpStatusCode.NotFound, missingPage.Status);
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
        Assert.Equal(404, missing.Json!["model"]!["statusCode"]!.GetValue<int>());
    }

    [Fact]
    public async Task AMavenDeploymentThroughTheTwinKeepsThePostedCoordinatesWhenRefused()
    {
        var fields = new Dictionary<string, string> { { "groupId", "org.acme" }, { "artifactId", "lib" }, { "version", "2.0" }, { "packaging", "jar" } };

        var deployed = await PostAsync(_anonymous, "/api/ui/r/libs/maven-upload", Multipart("file", "lib.jar", Encoding.UTF8.GetBytes("jar bytes"), fields), key: _aliceKey);
        Assert.Equal(HttpStatusCode.OK, deployed.Status);
        Assert.Equal("org.acme:lib", deployed.Json!["model"]!["deployedName"]!.GetValue<string>());

        var noFile = await PostAsync(_anonymous, "/api/ui/r/libs/maven-upload", Multipart("file", null, null, fields), key: _aliceKey);
        Assert.Equal(HttpStatusCode.BadRequest, noFile.Status);
        Assert.Equal("org.acme", noFile.Json!["model"]!["posted"]!["groupId"]!.GetValue<string>());
        Assert.DoesNotContain(_aliceKey, noFile.Body, StringComparison.Ordinal);
    }

    // ---- Docker ----

    private async Task PushImageAsync(string image, string tag, string seed)
    {
        using var client = _app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:" + _aliceKey)));
        var content = OciTestImage.Create(seed, 2);
        foreach (var blob in new[] { content.Config }.Concat(content.Layers))
        {
            using var response = await client.PostAsync($"/v2/reg/{image}/blobs/uploads/?digest={OciTestImage.Digest(blob)}", new ByteArrayContent(blob), Token);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        using var manifest = new ByteArrayContent(content.Manifest);
        manifest.Headers.ContentType = MediaTypeHeaderValue.Parse(OciTestImage.ManifestType);
        using var put = await client.PutAsync($"/v2/reg/{image}/manifests/{tag}", manifest, Token);
        Assert.Equal(HttpStatusCode.Created, put.StatusCode);
    }

    [Fact]
    public async Task TheDockerTwinsCarryWhatThePagesShow()
    {
        await PushImageAsync("team/app", "1.0", "a");

        var listPage = await GetAsync(_anonymous, "/r/reg/docker-images");
        var list = await GetAsync(_anonymous, "/api/ui/r/reg/docker-images");
        var imagePage = await GetAsync(_anonymous, "/r/reg/docker-images/team/app");
        var image = await GetAsync(_anonymous, "/api/ui/r/reg/docker-images/team/app");

        Assert.Equal(1, list.Json!["model"]!["result"]!["total"]!.GetValue<int>());
        Assert.NotEmpty(list.Json["model"]!["commandTabs"]!.AsArray());
        Assert.Contains("team/app", listPage.Body);

        Assert.Equal(HttpStatusCode.OK, image.Status);
        Assert.Equal("1.0", image.Json!["model"]!["info"]!["tags"]![0]!["name"]!.GetValue<string>());
        Assert.NotEmpty(image.Json["model"]!["pullTabs"]!.AsArray());
        Assert.Contains("1.0", imagePage.Body);

        // One manifest in full: the digest of the image is a query value, as on the page.
        var digest = image.Json["model"]!["info"]!["tags"]![0]!["digest"]!.GetValue<string>();
        var manifest = await GetAsync(_anonymous, "/api/ui/r/reg/docker-images/team/app?digest=" + digest);
        Assert.Equal(HttpStatusCode.OK, manifest.Status);
        Assert.Equal(digest, manifest.Json!["model"]!["selected"]!["summary"]!["digest"]!.GetValue<string>());
    }

    [Fact]
    public async Task AnUnknownImageOrManifestAndAnInvalidDigestAreNotFoundOnThePageAndOnTheTwin()
    {
        await PushImageAsync("team/app", "1.0", "a");
        var unknownDigest = "sha256:" + new string('0', 64);

        foreach (var suffix in new[] { "nobody/here", "team/app?digest=" + unknownDigest, "team/app?digest=not-a-digest" })
        {
            var page = await GetAsync(_anonymous, "/r/reg/docker-images/" + suffix);
            var api = await GetAsync(_anonymous, "/api/ui/r/reg/docker-images/" + suffix);

            Assert.Equal(HttpStatusCode.NotFound, page.Status);
            Assert.Equal(HttpStatusCode.NotFound, api.Status);
        }
    }
}
