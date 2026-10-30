using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// The web pages of a Docker repository (UI counterpart of the registry): create, browse, image and manifest pages (tags, index
/// platforms, layers with sizes, the commands to copy), the delete tools (tag, manifest, unused layers, image) behind the
/// Delete permission and the password, and the way the NuGet pages behave in a Docker repository.
/// </summary>
public class DockerPagesTests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly Browser _alice;
    private readonly Browser _bob;
    private readonly Browser _root;
    private readonly Browser _anonymous;
    private string _aliceKey = string.Empty;

    public DockerPagesTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "true" }, { "ApiKey", "" } });
        _alice = new Browser(_app);
        _bob = new Browser(_app);
        _root = new Browser(_app);
        _anonymous = new Browser(_app);
        SetUpAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _alice.Dispose();
        _bob.Dispose();
        _root.Dispose();
        _anonymous.Dispose();
        _app.Dispose();
    }

    private async Task SetUpAsync()
    {
        await _app.CreateDockerRepositoryAsync("reg", cancellationToken: CancellationToken.None);
        await CreateUserAsync(_app, "alice");
        await CreateUserAsync(_app, "bob");
        await CreateUserAsync(_app, "root", administrator: true);
        await SignInAsync(_alice, "alice");
        await SignInAsync(_bob, "bob");
        await SignInAsync(_root, "root");

        using var scope = _app.Services.CreateScope();
        _aliceKey = await scope.ServiceProvider.GetRequiredService<ApiKeyService>()
            .CreateAsync((await FindAsync(_app, "alice"))!.Id, "t", ApiKeyScopes.All, "*", null, CancellationToken.None);
    }

    private HttpClient Client()
    {
        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:" + _aliceKey)));

        return client;
    }

    private async Task PushBlobAsync(HttpClient client, string image, byte[] content)
    {
        var digest = OciTestImage.Digest(content);
        using var response = await client.PostAsync($"/v2/reg/{image}/blobs/uploads/?digest={digest}", new ByteArrayContent(content), TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"blob: {response.StatusCode}");
    }

    private async Task PutManifestAsync(HttpClient client, string image, string reference, byte[] manifest, string type = OciTestImage.ManifestType)
    {
        using var content = new ByteArrayContent(manifest);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(type);
        using var response = await client.PutAsync($"/v2/reg/{image}/manifests/{reference}", content, TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"manifest: {response.StatusCode}");
    }

    private async Task<OciTestImage> PushAsync(string image, string tag, string seed, int layers = 2, string architecture = "amd64")
    {
        using var client = Client();
        var content = OciTestImage.Create(seed, layers, architecture: architecture);
        await PushBlobAsync(client, image, content.Config);
        foreach (var layer in content.Layers) await PushBlobAsync(client, image, layer);
        await PutManifestAsync(client, image, tag, content.Manifest);

        return content;
    }

    // ---- create ----

    [Fact]
    public async Task AnAdministratorCreatesADockerRepositoryAndItsLinksWork()
    {
        var page = await _root.GetPageAsync("admin/repositories");
        Assert.Contains("id=\"format-create-form-docker\"", page);

        var fields = new Dictionary<string, string> { { "format", "Docker" }, { "name", "containers" }, { "anonymousRead", "true" } };
        using var created = await _root.PostAsync("admin/repositories", fields, action: "admin/repositories?handler=Format");
        var createdBody = await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True((int)created.StatusCode is 200 or 302, created.StatusCode.ToString());

        var listing = await _root.GetPageAsync("admin/repositories");
        Assert.True(listing.Contains("data-repository=\"containers\""), createdBody.Length > 3000 ? System.Text.RegularExpressions.Regex.Match(createdBody, "alert[^>]*>([^<]*)<").Value : createdBody);
        var line = listing.Substring(listing.IndexOf("data-repository=\"containers\"", StringComparison.Ordinal));
        line = line.Substring(0, line.IndexOf("</tr>", StringComparison.Ordinal));
        Assert.Contains("http://localhost/v2/containers/", line);
        Assert.Contains("class=\"repository-browse\" href=\"/r/containers/docker-images\"", line);
        Assert.DoesNotContain("v3/index.json", line);
        Assert.DoesNotContain("repository-upload", line);

        Assert.Contains("docker-images", await _root.GetPageAsync("r/containers/docker-images"));

        var edit = await _root.GetPageAsync("admin/repositories/containers");
        Assert.Contains("http://localhost/v2/containers/", edit);
    }

    // ---- browse ----

    [Fact]
    public async Task TheBrowsePageListsImagesSearchesAndShowsTheCommands()
    {
        await PushAsync("team/app", "1.0", "a");
        await PushAsync("tools/cli", "latest", "b");

        var all = await _anonymous.GetPageAsync("r/reg/docker-images");
        Assert.Contains("2 images", all);
        Assert.Contains("data-package=\"team/app\"", all);
        Assert.Contains("data-package=\"tools/cli\"", all);
        Assert.Contains("href=\"/r/reg/docker-images/team/app\"", all);
        Assert.Contains("docker login localhost", all);
        Assert.Contains("docker pull localhost/reg/team/app:latest", all);
        Assert.Contains("docker push localhost/reg/team/app:latest", all);
        Assert.Contains("docker buildx build --platform", all);
        Assert.Contains("insecure-registries", all);

        var found = await _anonymous.GetPageAsync("r/reg/docker-images?q=cli");
        Assert.Contains("1 image", found);
        Assert.DoesNotContain("data-package=\"team/app\"", found);

        var none = await _anonymous.GetPageAsync("r/reg/docker-images?q=zzzz");
        Assert.Contains("id=\"docker-no-results\"", none);
    }

    [Fact]
    public async Task TheFrontPageAndTheHeaderOfADockerRepositoryLeadToItsBrowsePage()
    {
        using var front = await _anonymous.GetAsync("r/reg");
        var html = await _anonymous.GetPageAsync("r/reg/docker-images");

        Assert.Equal(HttpStatusCode.Redirect, front.StatusCode);
        Assert.EndsWith("/r/reg/docker-images", front.Headers.Location?.OriginalString);
        Assert.Contains("href=\"/r/reg/docker-images\"", html);
        Assert.DoesNotContain("href=\"/r/reg/upload\"", html);
        Assert.DoesNotContain("href=\"/r/reg/packages\"", html);
    }

    // ---- image page ----

    [Fact]
    public async Task TheImagePageShowsTagsManifestsAndTheCommands()
    {
        var one = await PushAsync("team/app", "1.0", "one");
        await PushAsync("team/app", "2.0", "two", architecture: "arm64");

        var html = await _anonymous.GetPageAsync("r/reg/docker-images/team/app");

        Assert.Contains("data-tag=\"1.0\"", html);
        Assert.Contains("data-tag=\"2.0\"", html);
        Assert.Contains($"data-digest=\"{one.ManifestDigest}\"", html);
        Assert.Contains("2 tags", html);
        Assert.Contains("2 manifests", html);
        Assert.Contains("docker pull localhost/reg/team/app:", html);
        Assert.Contains("docker pull localhost/reg/team/app@sha256:", html);
        Assert.DoesNotContain("docker-manage-link", html);
    }

    [Fact]
    public async Task TheManifestPageListsTheConfigAndEveryLayerWithItsSize()
    {
        var image = await PushAsync("team/app", "1.0", "layers", layers: 3);

        var html = await _anonymous.GetPageAsync($"r/reg/docker-images/team/app?digest={image.ManifestDigest}");

        Assert.Contains($"data-config=\"{image.ConfigDigest}\"", html);
        foreach (var digest in image.LayerDigests) Assert.Contains($"data-layer=\"{digest}\"", html);
        Assert.Contains($"{image.Layers.Sum(l => l.Length) + image.Config.Length:N0} bytes", html);
        Assert.Contains("id=\"docker-platform\">linux/amd64<", html);
        Assert.Contains("id=\"docker-manifest-json\"", html);
        Assert.Contains("&quot;schemaVersion&quot;: 2", html);
    }

    [Fact]
    public async Task TheManifestPageListsItsReferrersWithTheirArtifactType()
    {
        var subject = await PushAsync("team/app", "1.0", "subject");
        var sbom = OciTestImage.Create("sbom", subject: subject.ManifestDigest, artifactType: "application/vnd.example.sbom");
        using (var client = Client())
        {
            await PushBlobAsync(client, "team/app", sbom.Config);
            foreach (var layer in sbom.Layers) await PushBlobAsync(client, "team/app", layer);
            await PutManifestAsync(client, "team/app", "sbom", sbom.Manifest);
        }

        var html = await _anonymous.GetPageAsync($"r/reg/docker-images/team/app?digest={subject.ManifestDigest}");

        Assert.Contains("id=\"docker-referrers\"", html);
        Assert.Contains($"data-referrer=\"{sbom.ManifestDigest}\"", html);
        Assert.Contains("application/vnd.example.sbom", html);

        var own = await _anonymous.GetPageAsync($"r/reg/docker-images/team/app?digest={sbom.ManifestDigest}");
        Assert.DoesNotContain("id=\"docker-referrers\"", own);
    }

    [Fact]
    public async Task TheReferrersCanBeFilteredByArtifactTypeLikeTheReferrersApi()
    {
        var subject = await PushAsync("team/app", "1.0", "subject");
        var sbom = OciTestImage.Create("sbom", subject: subject.ManifestDigest, artifactType: "application/vnd.example.sbom");
        var signature = OciTestImage.Create("sig", subject: subject.ManifestDigest, artifactType: "application/vnd.example.signature");
        using (var client = Client())
        {
            foreach (var (image, tag) in new[] { (sbom, "sbom"), (signature, "sig") })
            {
                await PushBlobAsync(client, "team/app", image.Config);
                foreach (var layer in image.Layers) await PushBlobAsync(client, "team/app", layer);
                await PutManifestAsync(client, "team/app", tag, image.Manifest);
            }
        }

        var all = await _anonymous.GetPageAsync($"r/reg/docker-images/team/app?digest={subject.ManifestDigest}");
        Assert.Contains($"data-referrer=\"{sbom.ManifestDigest}\"", all);
        Assert.Contains($"data-referrer=\"{signature.ManifestDigest}\"", all);

        var filtered = await _anonymous.GetPageAsync($"r/reg/docker-images/team/app?digest={subject.ManifestDigest}&artifactType=application/vnd.example.sbom");
        Assert.Contains($"data-referrer=\"{sbom.ManifestDigest}\"", filtered);
        Assert.DoesNotContain($"data-referrer=\"{signature.ManifestDigest}\"", filtered);
        Assert.Contains("id=\"docker-referrers-filter\"", filtered);

        var none = await _anonymous.GetPageAsync($"r/reg/docker-images/team/app?digest={subject.ManifestDigest}&artifactType=application/vnd.example.other");
        Assert.DoesNotContain("data-referrer=", none);
        Assert.Contains("id=\"docker-referrers-filter\"", none);
    }

    [Fact]
    public async Task AnIndexPageListsItsPlatformsAndLinksToTheirManifests()
    {
        var amd64 = await PushAsync("team/app", "amd64", "amd", architecture: "amd64");
        var arm64 = await PushAsync("team/app", "arm64", "arm", architecture: "arm64");
        var (index, indexDigest) = OciTestImage.Index((amd64, "linux", "amd64", null), (arm64, "linux", "arm64", "v8"));
        using (var client = Client())
        {
            await PutManifestAsync(client, "team/app", "multi", index, OciTestImage.IndexType);
        }

        var html = await _anonymous.GetPageAsync($"r/reg/docker-images/team/app?digest={indexDigest}");

        Assert.Contains("data-platform=\"linux/amd64\"", html);
        Assert.Contains("data-platform=\"linux/arm64/v8\"", html);
        Assert.Contains($"digest={Uri.EscapeDataString(amd64.ManifestDigest)}", html);

        var overview = await _anonymous.GetPageAsync("r/reg/docker-images/team/app");
        Assert.Contains("index (multi-platform)", overview);
        // The children's platform comes from the index that lists them.
        Assert.Contains("linux/arm64/v8", overview);
    }

    [Fact]
    public async Task AnUnknownImageOrManifestAndAnInvalidNameAreNotFound()
    {
        await PushAsync("team/app", "1.0", "x");

        using var image = await _anonymous.GetAsync("r/reg/docker-images/team/none");
        using var manifest = await _anonymous.GetAsync("r/reg/docker-images/team/app?digest=" + OciTestImage.Digest(new byte[] { 1 }));
        using var invalid = await _anonymous.GetAsync("r/reg/docker-images/Team/App");
        using var badDigest = await _anonymous.GetAsync("r/reg/docker-images/team/app?digest=sha256:abc");

        Assert.Equal(HttpStatusCode.NotFound, image.StatusCode);
        Assert.Contains("docker-image-missing", await image.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, manifest.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, badDigest.StatusCode);
    }

    [Fact]
    public async Task APrivateRepositoryHidesItsPagesFromAnonymousVisitors()
    {
        await PushAsync("team/app", "1.0", "x");
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().SetAnonymousReadAsync("reg", false, CancellationToken.None);
        }

        using var anonymous = await _anonymous.GetAsync("r/reg/docker-images/team/app");
        using var list = await _anonymous.GetAsync("r/reg/docker-images");
        using var plainUser = await _alice.GetAsync("r/reg/docker-images");
        var administrator = await _root.GetPageAsync("r/reg/docker-images");

        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, list.StatusCode);
        // Reading is a permission of its own: a signed-in user without it is told "not found", an administrator reads.
        Assert.Equal(HttpStatusCode.NotFound, plainUser.StatusCode);
        Assert.Contains("data-package=\"team/app\"", administrator);
    }

    [Fact]
    public async Task TheDockerPagesAreNotInANuGetRepositoryAndTheNuGetPagesAreNotInADockerOne()
    {
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRepositoryService>().CreateAsync("feed", RepositoryFormat.NuGet, RepositoryKind.Local, CancellationToken.None);
        }

        foreach (var path in new[] { "r/feed/docker-images", "r/feed/docker-images/team/app", "r/feed/docker-manage/team/app", "r/reg/upload", "r/reg/statistics", "docker-images" })
        {
            using var response = await _alice.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, path + " " + response.StatusCode);
        }
    }

    [Fact]
    public async Task TheNuGetUploadPageDoesNotOfferADockerRepository()
    {
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRepositoryService>().CreateAsync("feed", RepositoryFormat.NuGet, RepositoryKind.Local, CancellationToken.None);
        }

        var html = await _alice.GetPageAsync("upload");

        Assert.Contains("id=\"upload-repository\"", html);
        var picker = html.Substring(html.IndexOf("id=\"upload-repository\"", StringComparison.Ordinal));
        picker = picker.Substring(0, picker.IndexOf("</p>", StringComparison.Ordinal));
        Assert.Contains("data-repository=\"feed\"", picker);
        Assert.DoesNotContain("data-repository=\"reg\"", picker);
    }

    // ---- manage ----

    private Task<HttpResponseMessage> ManageAsync(Browser browser, string handler, Dictionary<string, string> fields, string image = "team/app")
        => browser.PostAsync($"r/reg/docker-manage/{image}", fields, action: $"r/reg/docker-manage/{image}?handler={handler}", tokenPage: $"r/reg/docker-manage/{image}");

    private async Task<bool> ManifestExistsAsync(string digest, string image = "team/app")
    {
        using var client = Client();
        using var response = await client.GetAsync($"/v2/reg/{image}/manifests/{digest}", TestContext.Current.CancellationToken);

        return response.StatusCode == HttpStatusCode.OK;
    }

    private async Task<bool> TagExistsAsync(string tag, string image = "team/app")
    {
        using var client = Client();
        using var response = await client.GetAsync($"/v2/reg/{image}/manifests/{tag}", TestContext.Current.CancellationToken);

        return response.StatusCode == HttpStatusCode.OK;
    }

    [Fact]
    public async Task OnlyUsersWithTheDeletePermissionOpenTheManagePage()
    {
        await PushAsync("team/app", "1.0", "x");

        using var alice = await _alice.GetAsync("r/reg/docker-manage/team/app");
        using var admin = await _root.GetAsync("r/reg/docker-manage/team/app");
        using var anonymous = await _anonymous.GetAsync("r/reg/docker-manage/team/app");
        using var missing = await _alice.GetAsync("r/reg/docker-manage/team/none");
        using var invalid = await _alice.GetAsync("r/reg/docker-manage/Team/App");

        Assert.Equal(HttpStatusCode.OK, alice.StatusCode);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);

        // The image page offers the tools to those who hold Delete, and only to them.
        Assert.Contains("id=\"docker-manage-link\"", await _alice.GetPageAsync("r/reg/docker-images/team/app"));
        Assert.DoesNotContain("id=\"docker-manage-link\"", await _anonymous.GetPageAsync("r/reg/docker-images/team/app"));

        // Taking the seeded "authenticated users deploy" permission away takes the tools away from a plain user, not from an administrator.
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAccessAdministration>().DeleteTargetAsync(BuiltInNames.AuthenticatedDeploy, "test", CancellationToken.None);
        }

        using var bob = await _bob.GetAsync("r/reg/docker-manage/team/app");
        using var aliceNow = await _alice.GetAsync("r/reg/docker-manage/team/app");
        using var adminNow = await _root.GetAsync("r/reg/docker-manage/team/app");
        Assert.Equal(HttpStatusCode.Forbidden, bob.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, aliceNow.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminNow.StatusCode);
        Assert.DoesNotContain("id=\"docker-manage-link\"", await _alice.GetPageAsync("r/reg/docker-images/team/app"));
    }

    [Fact]
    public async Task DeletingATagNeedsThePasswordAndKeepsTheManifest()
    {
        var image = await PushAsync("team/app", "1.0", "x");

        using var wrong = await ManageAsync(_alice, "DeleteTag", new Dictionary<string, string> { { "tag", "1.0" }, { "password", "wrong" } });
        Assert.Contains("confirmation is incorrect", await wrong.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(await TagExistsAsync("1.0"));

        using var right = await ManageAsync(_alice, "DeleteTag", new Dictionary<string, string> { { "tag", "1.0" }, { "password", Password } });
        Assert.Contains("Tag 1.0 is deleted", await right.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.False(await TagExistsAsync("1.0"));
        Assert.True(await ManifestExistsAsync(image.ManifestDigest));
    }

    [Fact]
    public async Task DeletingAManifestRemovesItsTagsAndTheLayersCanThenBePruned()
    {
        var image = await PushAsync("team/app", "1.0", "x");

        using var wrong = await ManageAsync(_alice, "DeleteManifest", new Dictionary<string, string> { { "digest", image.ManifestDigest }, { "password", "wrong" } });
        Assert.True(await ManifestExistsAsync(image.ManifestDigest));

        using var right = await ManageAsync(_alice, "DeleteManifest", new Dictionary<string, string> { { "digest", image.ManifestDigest }, { "password", Password } });
        Assert.Contains("manifest and its tags are deleted", await right.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.False(await ManifestExistsAsync(image.ManifestDigest));
        Assert.False(await TagExistsAsync("1.0"));

        // Inside the grace period the layers stay: a push in progress uploads them before its manifest.
        using var prune = await ManageAsync(_alice, "Prune", new Dictionary<string, string> { { "password", Password } });
        Assert.Contains("no unused layer old enough", await prune.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeletingTheImageRemovesItEntirelyAndItsLayersWhenNobodyElseHoldsThem()
    {
        var image = await PushAsync("team/app", "1.0", "x");
        await PushAsync("team/other", "1.0", "y");

        using var response = await ManageAsync(_alice, "DeleteImage", new Dictionary<string, string> { { "password", Password } });

        Assert.True((int)response.StatusCode is 200 or 302, response.StatusCode.ToString());
        using var scope = _app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepositoryContext>().Use((await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync("reg", CancellationToken.None))!);
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        Assert.False(await context.OciRepositories!.AnyAsync(i => i.Name == "team/app", TestContext.Current.CancellationToken));
        Assert.True(await context.OciRepositories!.AnyAsync(i => i.Name == "team/other", TestContext.Current.CancellationToken));
        Assert.False(await context.OciBlobs!.AnyAsync(b => b.Digest == image.LayerDigests[0], TestContext.Current.CancellationToken));
        Assert.Empty(await context.OciManifests!.Where(m => m.Digest == image.ManifestDigest).ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ALayerSharedWithAnotherImageSurvivesTheDeletionOfOne()
    {
        // The same layer bytes pushed under two images are stored once and kept while one image still holds them.
        using var client = Client();
        var content = OciTestImage.Create("shared");
        foreach (var image in new[] { "team/a", "team/b" })
        {
            await PushBlobAsync(client, image, content.Config);
            await PushBlobAsync(client, image, content.Layers[0]);
            await PutManifestAsync(client, image, "1", content.Manifest);
        }

        using var response = await ManageAsync(_alice, "DeleteImage", new Dictionary<string, string> { { "password", Password } }, image: "team/a");
        Assert.True((int)response.StatusCode is 200 or 302);

        using var blob = await client.GetAsync($"/v2/reg/team/b/blobs/{content.LayerDigests[0]}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, blob.StatusCode);
        Assert.Equal(content.Layers[0], await blob.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheManagePageIsRefusedWritesInReadOnlyModeAndWhileTheGateIsClosed()
    {
        var image = await PushAsync("team/app", "1.0", "x");
        var gate = _app.Services.GetRequiredService<MaintenanceGate>();
        gate.Close("Copying the database");
        try
        {
            using var response = await ManageAsync(_alice, "DeleteTag", new Dictionary<string, string> { { "tag", "1.0" }, { "password", Password } });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        finally
        {
            gate.Open();
        }

        Assert.True(await TagExistsAsync("1.0"));
        Assert.True(await ManifestExistsAsync(image.ManifestDigest));
    }
}
