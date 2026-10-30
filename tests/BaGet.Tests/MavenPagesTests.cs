using System;
using System.Collections.Generic;
using System.IO;
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
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Tests;

/// <summary>
/// The web pages of a Maven repository (UI counterpart of the protocol): create and set up, browse, artifact page, upload, manage
/// (owners, delete file, version and artifact, snapshot clean-up), My packages, and the way the NuGet pages behave in a
/// repository of another format.
/// </summary>
public class MavenPagesTests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly Browser _alice;
    private readonly Browser _bob;
    private readonly Browser _root;
    private readonly Browser _anonymous;
    private string _aliceKey = string.Empty;

    private readonly ITestOutputHelper _output;

    public MavenPagesTests(ITestOutputHelper output)
    {
        _output = output;
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
        await _app.CreateMavenRepositoryAsync("libs", policy: "Both", cancellationToken: CancellationToken.None);
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

    private async Task PutAsync(string path, string content, string repo = "libs")
    {
        using var client = _app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/r/{repo}/maven/{path}") { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(content)) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:" + _aliceKey)));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"PUT {path}: {response.StatusCode}");
    }

    private Task DeployAsync(string artifact, string version, string content = "bytes", string group = "org.acme")
    {
        var path = $"{group.Replace('.', '/')}/{artifact}/{version}/{artifact}-{version}";

        return DeployFilesAsync(path, content, MavenPom.Generate(group, artifact, version, "jar", "The " + artifact, "About " + artifact));
    }

    private async Task DeployFilesAsync(string path, string jar, string pom)
    {
        await PutAsync(path + ".jar", jar);
        await PutAsync(path + ".pom", pom);
    }

    private async Task<MavenArtifactInfo?> ArtifactAsync(string group, string artifact, string repo = "libs")
    {
        using var scope = _app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepositoryContext>().Use((await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync(repo, CancellationToken.None))!);

        return await scope.ServiceProvider.GetRequiredService<IMavenRepository>().GetArtifactAsync(group, artifact, CancellationToken.None);
    }

    // ---- creating and setting up a repository ----

    [Fact]
    public async Task AnAdministratorCreatesAMavenRepositoryWithItsPolicyAndItsLinksWork()
    {
        var page = await _root.GetPageAsync("admin/repositories");
        Assert.Contains("id=\"format-create-form-maven\"", page);
        Assert.Contains("name=\"field:policy\"", page);

        var fields = new Dictionary<string, string>
        {
            { "format", "Maven" }, { "name", "snap" }, { "field:policy", "Snapshots" }, { "field:redeploy", "Yes" }, { "field:snapshotBuilds", "4" }, { "anonymousRead", "true" },
        };
        using var created = await _root.PostAsync("admin/repositories", fields, action: "admin/repositories?handler=Format");
        Assert.True((int)created.StatusCode is 200 or 302, created.StatusCode.ToString());

        using var scope = _app.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<IContext>().Repositories!.AsNoTracking().FirstAsync(r => r.Name == "snap", TestContext.Current.CancellationToken);
        var settings = MavenRepositorySettings.Parse(row.Settings);
        Assert.Equal(RepositoryFormat.Maven, row.Format);
        Assert.Equal(MavenPolicy.Snapshots, settings.Policy);
        Assert.True(settings.AllowRedeploy);
        Assert.Equal(4, settings.MaxSnapshotBuilds);

        var listing = await _root.GetPageAsync("admin/repositories");
        var line = listing.Substring(listing.IndexOf("data-repository=\"snap\"", StringComparison.Ordinal));
        line = line.Substring(0, line.IndexOf("</tr>", StringComparison.Ordinal));
        Assert.Contains("/r/snap/maven/", line);
        Assert.Contains("class=\"repository-browse\" href=\"/r/snap/maven-artifacts\"", line);
        Assert.Contains("class=\"repository-upload\" href=\"/r/snap/maven-upload\"", line);
        Assert.DoesNotContain("v3/index.json", line);

        Assert.Contains("maven-artifacts", await _root.GetPageAsync("r/snap/maven-artifacts"));
        Assert.Contains("maven-upload-form", await _root.GetPageAsync("r/snap/maven-upload"));
    }

    [Fact]
    public async Task AnInvalidPolicyOnTheCreateFormIsRefusedWithAMessage()
    {
        var fields = new Dictionary<string, string> { { "format", "Maven" }, { "name", "bad" }, { "field:policy", "Everything" }, { "field:snapshotBuilds", "x" }, { "anonymousRead", "true" } };

        using var response = await _root.PostAsync("admin/repositories", fields, action: "admin/repositories?handler=Format");

        Assert.Contains("policy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.OrdinalIgnoreCase);
        using var scope = _app.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<IContext>().Repositories!.AnyAsync(r => r.Name == "bad", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheEditPageShowsAndSavesTheMavenSettings()
    {
        var html = await _root.GetPageAsync("admin/repositories/libs");
        Assert.Contains("id=\"maven-settings-form\"", html);
        Assert.Contains("/r/libs/maven/", html);

        var fields = new Dictionary<string, string> { { "policy", "Releases" }, { "redeploy", "Yes" }, { "snapshotBuilds", "7" } };
        using var saved = await _root.PostAsync("admin/repositories/libs", fields, action: "admin/repositories/libs?handler=MavenSettings");
        Assert.True((int)saved.StatusCode is 200 or 302, saved.StatusCode.ToString());

        using var scope = _app.Services.CreateScope();
        var settings = (await scope.ServiceProvider.GetRequiredService<IMavenRepositoryAdministration>().GetSettingsAsync("libs", CancellationToken.None))!;
        Assert.Equal(MavenPolicy.Releases, settings.Policy);
        Assert.True(settings.AllowRedeploy);
        Assert.Equal(7, settings.MaxSnapshotBuilds);
        Assert.Contains(scope.ServiceProvider.GetRequiredService<IContext>().AccessAuditEntries!.AsNoTracking().ToList(), e => e.Action == "repository.maven-settings" && e.Actor == "root");

        using var refused = await _root.PostAsync("admin/repositories/libs", new Dictionary<string, string> { { "policy", "Nope" } }, action: "admin/repositories/libs?handler=MavenSettings");
        Assert.Contains("policy", await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.OrdinalIgnoreCase);

        using var anonymous = await _anonymous.GetAsync("admin/repositories/libs");
        Assert.NotEqual(HttpStatusCode.OK, anonymous.StatusCode);
    }

    // ---- browse ----

    [Fact]
    public async Task TheBrowsePageListsArtifactsSearchesAndShowsTheConfiguration()
    {
        await DeployAsync("lib", "1.0");
        await DeployAsync("other", "2.0", group: "com.example");

        var all = await _anonymous.GetPageAsync("r/libs/maven-artifacts");
        Assert.Contains("2 artifacts", all);
        Assert.Contains("data-package=\"org.acme:lib\"", all);
        Assert.Contains("data-package=\"com.example:other\"", all);
        Assert.Contains("http://localhost/r/libs/maven/", all);
        Assert.Contains("&lt;id&gt;libs&lt;/id&gt;", all);
        Assert.Contains("&lt;distributionManagement&gt;", all);
        Assert.Contains("&lt;server&gt;", all);
        Assert.Contains("mvn deploy", all);

        var found = await _anonymous.GetPageAsync("r/libs/maven-artifacts?q=about+other");
        Assert.Contains("1 artifact", found);
        Assert.DoesNotContain("data-package=\"org.acme:lib\"", found);

        var none = await _anonymous.GetPageAsync("r/libs/maven-artifacts?q=zzzzz");
        Assert.Contains("id=\"maven-no-results\"", none);
    }

    [Fact]
    public async Task TheFrontPageAndTheHeaderOfAMavenRepositoryLeadToItsBrowsePage()
    {
        using var front = await _anonymous.GetAsync("r/libs");
        var html = await _anonymous.GetPageAsync("r/libs/maven-artifacts");

        Assert.Equal(HttpStatusCode.Redirect, front.StatusCode);
        Assert.EndsWith("/r/libs/maven-artifacts", front.Headers.Location?.OriginalString);
        Assert.Contains("href=\"/r/libs/maven-artifacts\"", html);
        Assert.DoesNotContain("href=\"/r/libs/packages\"", html);
    }

    // ---- artifact page ----

    [Fact]
    public async Task TheArtifactPageShowsVersionsFilesChecksumsPomAndTheDependency()
    {
        await DeployAsync("lib", "1.0", "one");
        await DeployAsync("lib", "1.1", "two");
        await PutAsync("org/acme/lib/1.1/lib-1.1-sources.jar", "sources");

        var latest = await _anonymous.GetPageAsync("r/libs/maven-artifacts/org.acme/lib");
        Assert.Contains("id=\"maven-selected-version\">1.1<", latest);
        Assert.Contains("data-file=\"lib-1.1.jar\"", latest);
        Assert.Contains("data-file=\"lib-1.1-sources.jar\"", latest);
        Assert.Contains("data-file=\"lib-1.1.pom\"", latest);
        Assert.Contains(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes("two")).Select(b => b.ToString("x2")).Aggregate((a, b) => a + b), latest);
        Assert.Contains("href=\"http://localhost/r/libs/maven/org/acme/lib/1.1/lib-1.1.jar\"", latest);
        Assert.Contains("&lt;artifactId&gt;lib&lt;/artifactId&gt;", latest);
        Assert.Contains("implementation(&quot;org.acme:lib:1.1&quot;)", latest);
        Assert.Contains("id=\"maven-pom\"", latest);
        Assert.Contains("The lib", latest);
        Assert.Contains("data-version=\"1.0\"", latest);

        var older = await _anonymous.GetPageAsync("r/libs/maven-artifacts/org.acme/lib/1.0");
        Assert.Contains("id=\"maven-selected-version\">1.0<", older);
        Assert.DoesNotContain("lib-1.1-sources.jar", older.Substring(older.IndexOf("id=\"maven-files\"", StringComparison.Ordinal), 600));
    }

    [Fact]
    public async Task ASnapshotPageNamesItsBuilds()
    {
        await PutAsync("org/acme/lib/1.0-SNAPSHOT/lib-1.0-20240101.100000-1.jar", "a");
        await PutAsync("org/acme/lib/1.0-SNAPSHOT/lib-1.0-20240102.100000-2.jar", "b");

        var html = await _anonymous.GetPageAsync("r/libs/maven-artifacts/org.acme/lib/1.0-SNAPSHOT");

        Assert.Contains("id=\"maven-snapshot-label\"", html);
        Assert.Contains("snapshot, 2 builds", html);
    }

    [Fact]
    public async Task AnUnknownArtifactOrVersionAndAnInvalidNameAreNotFound()
    {
        await DeployAsync("lib", "1.0");

        using var artifact = await _anonymous.GetAsync("r/libs/maven-artifacts/org.acme/none");
        using var version = await _anonymous.GetAsync("r/libs/maven-artifacts/org.acme/lib/9.9");
        using var invalid = await _anonymous.GetAsync("r/libs/maven-artifacts/org..acme/lib");

        Assert.Equal(HttpStatusCode.NotFound, artifact.StatusCode);
        Assert.Contains("maven-artifact-missing", await artifact.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, version.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
    }

    [Fact]
    public async Task APrivateRepositoryHidesItsPagesFromAnonymousVisitorsAndOtherUsers()
    {
        await DeployAsync("lib", "1.0");
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().SetAnonymousReadAsync("libs", false, CancellationToken.None);
        }

        using var anonymous = await _anonymous.GetAsync("r/libs/maven-artifacts/org.acme/lib");
        using var stranger = await _bob.GetAsync("r/libs/maven-artifacts/org.acme/lib");
        using var strangerList = await _bob.GetAsync("r/libs/maven-artifacts");

        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, strangerList.StatusCode);
    }

    [Fact]
    public async Task TheMavenPagesAreNotInANuGetRepositoryAndTheNuGetPagesAreNotInAMavenOne()
    {
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRepositoryService>().CreateAsync("feed", RepositoryFormat.NuGet, RepositoryKind.Local, CancellationToken.None);
        }

        foreach (var path in new[] { "r/feed/maven-artifacts", "r/feed/maven-upload", "r/feed/maven-artifacts/org.acme/lib", "r/feed/maven-manage/org.acme/lib", "r/libs/upload", "r/libs/statistics", "maven-artifacts" })
        {
            using var response = await _alice.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, path + " " + response.StatusCode);
        }
    }

    // ---- upload ----

    private async Task<HttpResponseMessage> UploadAsync(Browser browser, Dictionary<string, string> fields, string content = "jar bytes", string name = "lib.jar", string page = "r/libs/maven-upload")
        => await browser.PostFileAsync(page, "file", name, new MemoryStream(Encoding.UTF8.GetBytes(content)), fields);

    private static Dictionary<string, string> Coordinates(string artifact = "lib", string version = "1.0", string group = "org.acme", string? classifier = null)
    {
        var fields = new Dictionary<string, string> { { "groupId", group }, { "artifactId", artifact }, { "version", version }, { "packaging", "jar" } };
        if (classifier != null) fields["classifier"] = classifier;

        return fields;
    }

    [Fact]
    public async Task ASignedInUserDeploysFromTheBrowserAndBecomesTheOwner()
    {
        using var response = await UploadAsync(_alice, Coordinates());

        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Deployed org.acme:lib 1.0", html);
        Assert.Contains("href=\"/r/libs/maven-artifacts/org.acme/lib/1.0\"", html);

        var detail = await ArtifactAsync("org.acme", "lib");
        Assert.Equal("1.0", Assert.Single(detail!.Versions).Version);
        Assert.Equal(2, Assert.Single(detail.Versions).FileCount);
        Assert.Equal("alice", Assert.Single(await OwnersAsync(_app, "org.acme:lib", "libs")).OwnerName);
    }

    [Fact]
    public async Task ADeployWithAClassifierAndAPomOfItsOwnKeepsThePomAndAddsNoGeneratedOne()
    {
        var pom = MavenPom.Generate("org.acme", "lib", "1.0", "jar", "Custom name", "Custom description");
        var fields = Coordinates();
        using var withPom = new MultipartFormDataContent();
        foreach (var pair in fields) withPom.Add(new StringContent(pair.Value), pair.Key);
        withPom.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("jar")), "file", "lib.jar");
        withPom.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(pom)), "pom", "pom.xml");
        withPom.Add(new StringContent(await _alice.TokenAsync("r/libs/maven-upload")), "__RequestVerificationToken");

        using var response = await _alice.SendAsync(new HttpRequestMessage(HttpMethod.Post, "r/libs/maven-upload") { Content = withPom });
        using var sources = await UploadAsync(_alice, Coordinates(classifier: "sources"), "src", "src.jar");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sources.StatusCode);
        var page = await _anonymous.GetPageAsync("r/libs/maven-artifacts/org.acme/lib/1.0");
        Assert.Contains("Custom name", page);
        Assert.Contains("data-file=\"lib-1.0-sources.jar\"", page);
    }

    [Fact]
    public async Task APolicyRefusalAnInvalidNameAndAMissingFileAreShownWithTheirStatus()
    {
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IMavenRepositoryAdministration>()
                .SaveSettingsAsync("libs", new MavenRepositorySettings { Policy = MavenPolicy.Releases }, "root", CancellationToken.None);
        }

        using var snapshot = await UploadAsync(_alice, Coordinates(version: "2.0-SNAPSHOT"));
        using var badGroup = await UploadAsync(_alice, Coordinates(group: "org..acme"));
        using var noFile = await _alice.PostFileAsync("r/libs/maven-upload", "file", "empty.jar", new MemoryStream(), Coordinates());

        Assert.Equal(HttpStatusCode.Conflict, snapshot.StatusCode);
        Assert.Contains("release", await snapshot.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, badGroup.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noFile.StatusCode);
        Assert.Null(await ArtifactAsync("org.acme", "lib"));
    }

    [Fact]
    public async Task AnotherUsersArtifactCannotBeDeployedToFromTheBrowser()
    {
        using (await UploadAsync(_alice, Coordinates())) { }

        using var bob = await UploadAsync(_bob, Coordinates(version: "1.1"));
        using var root = await UploadAsync(_root, Coordinates(version: "1.2"));

        Assert.Equal(HttpStatusCode.Forbidden, bob.StatusCode);
        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
        Assert.Equal(new[] { "1.2", "1.0" }, (await ArtifactAsync("org.acme", "lib"))!.Versions.Select(v => v.Version));
    }

    [Fact]
    public async Task AVisitorWhoIsNotSignedInIsSentToSignInAndNothingIsDeployed()
    {
        using var page = await _anonymous.GetAsync("r/libs/maven-upload");
        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var post = await _anonymous.PostFileAsync("r/libs/maven-upload", "file", "lib.jar", new MemoryStream(new byte[] { 1 }), Coordinates(), tokenPage: "account/login");

        Assert.Contains("id=\"upload-sign-in\"", html);
        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        Assert.Null(await ArtifactAsync("org.acme", "lib"));
    }

    [Fact]
    public async Task TheUploadPageShowsTheSettingsXmlAndDistributionManagementSnippets()
    {
        var html = await _alice.GetPageAsync("r/libs/maven-upload");

        Assert.Contains("id=\"maven-settings-xml\"", html);
        Assert.Contains("id=\"maven-distribution-management\"", html);
        Assert.Contains("&lt;snapshotRepository&gt;", html);
        Assert.Contains("http://localhost/r/libs/maven/", html);
    }

    [Fact]
    public async Task ReadOnlyModeAndAServerWithNothingToAuthenticateWithCloseTheUploadPage()
    {
        using var readOnly = new BaGetApplication(_output, configOverrides: new Dictionary<string, string> { { "ApiKey", "k" }, { "IsReadOnlyMode", "true" } });
        await readOnly.CreateMavenRepositoryAsync("libs", cancellationToken: CancellationToken.None);
        using var open = new BaGetApplication(_output);
        await open.CreateMavenRepositoryAsync("libs", cancellationToken: CancellationToken.None);
        using var readOnlyBrowser = new Browser(readOnly);
        using var openBrowser = new Browser(open);

        Assert.Contains("id=\"upload-read-only\"", await readOnlyBrowser.GetPageAsync("r/libs/maven-upload"));
        Assert.Contains("id=\"upload-no-api-key\"", await openBrowser.GetPageAsync("r/libs/maven-upload"));
    }

    // ---- manage ----

    private Task<HttpResponseMessage> ManageAsync(Browser browser, string handler, Dictionary<string, string> fields, string artifact = "org.acme/lib", string query = "")
        => browser.PostAsync($"r/libs/maven-manage/{artifact}{query}", fields, action: $"r/libs/maven-manage/{artifact}?handler={handler}" + (query.Length > 0 ? "&" + query.TrimStart('?') : string.Empty), tokenPage: $"r/libs/maven-manage/{artifact}");

    [Fact]
    public async Task OnlyOwnersAndAdministratorsOpenTheManagePage()
    {
        await DeployAsync("lib", "1.0");

        using var owner = await _alice.GetAsync("r/libs/maven-manage/org.acme/lib");
        using var admin = await _root.GetAsync("r/libs/maven-manage/org.acme/lib");
        using var other = await _bob.GetAsync("r/libs/maven-manage/org.acme/lib");
        using var anonymous = await _anonymous.GetAsync("r/libs/maven-manage/org.acme/lib");
        using var missing = await _alice.GetAsync("r/libs/maven-manage/org.acme/nothing");
        using var invalid = await _alice.GetAsync("r/libs/maven-manage/org..acme/lib");

        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        Assert.Contains("manage-as-admin", await admin.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
    }

    [Fact]
    public async Task TheManageLinkShowsToOwnersAndAdministratorsOnly()
    {
        await DeployAsync("lib", "1.0");

        Assert.Contains("id=\"maven-manage-link\"", await _alice.GetPageAsync("r/libs/maven-artifacts/org.acme/lib"));
        Assert.Contains("id=\"maven-manage-link\"", await _root.GetPageAsync("r/libs/maven-artifacts/org.acme/lib"));
        Assert.DoesNotContain("id=\"maven-manage-link\"", await _bob.GetPageAsync("r/libs/maven-artifacts/org.acme/lib"));
        Assert.DoesNotContain("id=\"maven-manage-link\"", await _anonymous.GetPageAsync("r/libs/maven-artifacts/org.acme/lib"));
        Assert.Contains("id=\"maven-owners\"", await _anonymous.GetPageAsync("r/libs/maven-artifacts/org.acme/lib"));
    }

    [Fact]
    public async Task OwnersCanBeAddedAndRemovedFromThePage()
    {
        await DeployAsync("lib", "1.0");

        using var add = await ManageAsync(_alice, "AddOwner", new Dictionary<string, string> { { "ownerName", "bob" } });
        Assert.Contains("bob is now an owner", await add.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(new[] { "alice", "bob" }, (await OwnersAsync(_app, "org.acme:lib", "libs")).Select(o => o.OwnerName).OrderBy(n => n));

        using var remove = await ManageAsync(_alice, "RemoveOwner", new Dictionary<string, string> { { "ownerName", "alice" } });
        Assert.Contains("no longer an owner", await remove.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var last = await ManageAsync(_bob, "RemoveOwner", new Dictionary<string, string> { { "ownerName", "bob" } });
        Assert.Contains("keeps at least one owner", await last.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ADeleteNeedsThePasswordAndTheFilesOfAVersionAreListed()
    {
        await DeployAsync("lib", "1.0");
        await DeployAsync("lib", "1.1");

        var listing = await _alice.GetPageAsync("r/libs/maven-manage/org.acme/lib?version=1.1");
        Assert.Contains("data-file=\"lib-1.1.jar\"", listing);
        Assert.Contains("data-file=\"lib-1.1.pom\"", listing);

        using var wrong = await ManageAsync(_alice, "DeleteFile", new Dictionary<string, string> { { "fileName", "lib-1.1.jar" }, { "password", "wrong" } }, query: "?version=1.1");
        Assert.Contains("confirmation is incorrect", await wrong.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, (await ArtifactAsync("org.acme", "lib"))!.Versions.First(v => v.Version == "1.1").FileCount);

        using var right = await ManageAsync(_alice, "DeleteFile", new Dictionary<string, string> { { "fileName", "lib-1.1.jar" }, { "password", Password } }, query: "?version=1.1");
        Assert.Contains("lib-1.1.jar is deleted", await right.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, (await ArtifactAsync("org.acme", "lib"))!.Versions.First(v => v.Version == "1.1").FileCount);
    }

    [Fact]
    public async Task AVersionAndThenTheWholeArtifactAreDeletedFromThePage()
    {
        await DeployAsync("lib", "1.0");
        await DeployAsync("lib", "1.1");

        using var wrong = await ManageAsync(_alice, "DeleteVersion", new Dictionary<string, string> { { "version", "1.0" }, { "password", "wrong" } });
        Assert.NotNull(await ArtifactAsync("org.acme", "lib"));
        Assert.Equal(2, (await ArtifactAsync("org.acme", "lib"))!.Versions.Count);

        using var version = await ManageAsync(_alice, "DeleteVersion", new Dictionary<string, string> { { "version", "1.0" }, { "password", Password } });
        Assert.Contains("org.acme:lib 1.0 is deleted", await version.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("1.1", Assert.Single((await ArtifactAsync("org.acme", "lib"))!.Versions).Version);

        using var all = await ManageAsync(_alice, "DeleteArtifact", new Dictionary<string, string> { { "password", Password } });
        Assert.Equal(HttpStatusCode.Redirect, all.StatusCode);
        Assert.Null(await ArtifactAsync("org.acme", "lib"));
        using var gone = await _anonymous.GetAsync("r/libs/maven/org/acme/lib/1.1/lib-1.1.jar");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task OldSnapshotBuildsAreCleanedUpFromThePage()
    {
        for (var build = 1; build <= 3; build++) await PutAsync($"org/acme/lib/1.0-SNAPSHOT/lib-1.0-20240101.10000{build}-{build}.jar", "build " + build);

        var page = await _alice.GetPageAsync("r/libs/maven-manage/org.acme/lib?version=1.0-SNAPSHOT");
        Assert.Contains("id=\"maven-cleanup-form\"", page);
        Assert.Contains("3 timestamped builds stored", page);

        using var zero = await ManageAsync(_alice, "Cleanup", new Dictionary<string, string> { { "version", "1.0-SNAPSHOT" }, { "keep", "0" }, { "password", Password } });
        Assert.Contains("at least 1", await zero.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        using var wrong = await ManageAsync(_alice, "Cleanup", new Dictionary<string, string> { { "version", "1.0-SNAPSHOT" }, { "keep", "1" }, { "password", "wrong" } });
        Assert.Contains("confirmation is incorrect", await wrong.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3, (await ArtifactAsync("org.acme", "lib"))!.Versions.Single().Builds);

        using var right = await ManageAsync(_alice, "Cleanup", new Dictionary<string, string> { { "version", "1.0-SNAPSHOT" }, { "keep", "1" }, { "password", Password } });
        Assert.Contains("Deleted 2 files", await right.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, (await ArtifactAsync("org.acme", "lib"))!.Versions.Single().Builds);
    }

    [Fact]
    public async Task AdministratorsManageWithoutOwningAndTheMaintenanceGateRefusesChanges()
    {
        await DeployAsync("lib", "1.0");

        using (var scope = _app.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<MaintenanceGate>().CloseUntilRestart("test");
        }

        using var response = await ManageAsync(_root, "DeleteArtifact", new Dictionary<string, string> { { "password", Password } });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(await ArtifactAsync("org.acme", "lib"));
    }

    // ---- my packages ----

    [Fact]
    public async Task MyPackagesListsTheArtifactsOfAMavenRepository()
    {
        await DeployAsync("lib", "1.0");
        await DeployAsync("lib", "1.1");

        var html = await _alice.GetPageAsync("account/packages");

        Assert.Contains("data-repository=\"libs\"", html);
        Assert.Contains("href=\"/r/libs/maven-artifacts/org.acme/lib\"", html);
        Assert.Contains("href=\"/r/libs/maven-manage/org.acme/lib\"", html);
        Assert.Contains("href=\"/r/libs/maven-upload\"", html);
    }

    [Fact]
    public async Task DeletingAnAccountWithItsSoleOwnedArtifactsDeletesTheArtifacts()
    {
        await DeployAsync("lib", "1.0");
        using (var scope = _app.Services.CreateScope())
        {
            var user = (await FindAsync(_app, "alice"))!;
            var result = await scope.ServiceProvider.GetRequiredService<AccountDeletionService>()
                .DeleteByAdministratorAsync(user, deleteSoleOwnedPackages: true, "root", CancellationToken.None);
            Assert.True(result.Result.Succeeded);
            Assert.Equal("org.acme:lib (libs)", Assert.Single(result.DeletedPackageIds));
        }

        Assert.Null(await ArtifactAsync("org.acme", "lib"));
    }

    private static async Task<IReadOnlyList<PackageOwner>> OwnersAsync(BaGetApplication app, string id, string repository)
        => await WithAsync(app, async services =>
        {
            services.GetRequiredService<IRepositoryContext>().Use((await services.GetRequiredService<IRepositoryService>().FindAsync(repository, CancellationToken.None))!);

            return await services.GetRequiredService<PackageOwnershipService>().GetOwnersAsync(id, CancellationToken.None);
        });
}
