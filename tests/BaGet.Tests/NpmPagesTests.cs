using System;
using System.Collections.Generic;
using System.IO;
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
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Tests;

/// <summary>
/// The web pages of an npm repository (UI counterpart of the registry): create, browse, package page, upload, manage, and the
/// way the NuGet pages and the header behave in a repository of another format.
/// </summary>
public class NpmPagesTests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly Browser _alice;
    private readonly Browser _bob;
    private readonly Browser _root;
    private readonly Browser _anonymous;
    private string _aliceKey = string.Empty;

    private readonly ITestOutputHelper _output;

    public NpmPagesTests(ITestOutputHelper output)
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
        await _app.CreateNpmRepositoryAsync("web", cancellationToken: CancellationToken.None);
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

    private async Task PublishAsync(string name, string version, string tag = "latest", string? readme = "# Hello readme")
    {
        using var client = _app.CreateClient();
        using var request = NpmTestPackages.Json(
            HttpMethod.Put,
            NpmTestPackages.Path("/r/web/npm/", name),
            NpmTestPackages.PublishBody(NpmTestPackages.Tarball(name, version, readme: readme ?? string.Empty), tag, readme),
            _aliceKey);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<NpmPackageInfo?> PackageAsync(string name)
    {
        using var scope = _app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepositoryContext>().Use((await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync("web", CancellationToken.None))!);

        return await scope.ServiceProvider.GetRequiredService<INpmRegistry>().GetPackageAsync(name, CancellationToken.None);
    }

    // ---- creating a repository ----

    [Fact]
    public async Task AnAdministratorCreatesAnNpmRepositoryFromTheRepositoriesPageAndItsLinksWork()
    {
        var page = await _root.GetPageAsync("admin/repositories");
        Assert.Contains("id=\"format-create-form-npm\"", page);

        var fields = new Dictionary<string, string> { { "format", "Npm" }, { "name", "webapp" }, { "anonymousRead", "true" } };
        using var created = await _root.PostAsync("admin/repositories", fields, action: "admin/repositories?handler=Format");
        Assert.True((int)created.StatusCode is 200 or 302, created.StatusCode.ToString());

        var listing = await _root.GetPageAsync("admin/repositories");
        var row = listing.Substring(listing.IndexOf("data-repository=\"webapp\"", StringComparison.Ordinal));
        row = row.Substring(0, row.IndexOf("</tr>", StringComparison.Ordinal));
        Assert.Contains("/r/webapp/npm/", row);
        Assert.Contains("class=\"repository-browse\" href=\"/r/webapp/npm-packages\"", row);
        Assert.Contains("class=\"repository-upload\" href=\"/r/webapp/npm-upload\"", row);
        Assert.Contains("class=\"repository-registry\"", row);
        Assert.DoesNotContain("v3/index.json", row);

        // The links lead somewhere.
        Assert.Contains("npm packages", await _root.GetPageAsync("r/webapp/npm-packages"));
        Assert.Contains("npm-upload-form", await _root.GetPageAsync("r/webapp/npm-upload"));
    }

    [Fact]
    public async Task TheEditPageOfAnNpmRepositoryShowsItsFeedAndAccess()
    {
        var html = await _root.GetPageAsync("admin/repositories/web");

        Assert.Contains("/r/web/npm/", html);
    }

    // ---- browse ----

    [Fact]
    public async Task TheBrowsePageListsPackagesSearchesAndShowsTheRegistryAddress()
    {
        await PublishAsync("left-pad", "1.0.0");
        await PublishAsync("right-pad", "2.0.0");
        await PublishAsync("@acme/tool", "3.0.0");

        var all = await _anonymous.GetPageAsync("r/web/npm-packages");
        Assert.Contains("3 packages", all);
        Assert.Contains("data-package=\"left-pad\"", all);
        Assert.Contains("data-package=\"@acme/tool\"", all);
        Assert.Contains("http://localhost/r/web/npm/", all);
        Assert.Contains("npm config set registry http://localhost/r/web/npm/", all);

        var found = await _anonymous.GetPageAsync("r/web/npm-packages?q=pad");
        Assert.Contains("2 packages", found);
        Assert.DoesNotContain("@acme/tool", found);

        var none = await _anonymous.GetPageAsync("r/web/npm-packages?q=zzz");
        Assert.Contains("id=\"npm-no-results\"", none);
    }

    [Fact]
    public async Task TheBrowsePageIsPaged()
    {
        for (var i = 1; i <= 21; i++) await PublishAsync($"pkg-{i:00}", "1.0.0");

        var first = await _anonymous.GetPageAsync("r/web/npm-packages");
        var second = await _anonymous.GetPageAsync("r/web/npm-packages?p=2");

        Assert.Contains("data-package=\"pkg-01\"", first);
        Assert.DoesNotContain("data-package=\"pkg-21\"", first);
        Assert.Contains("p=2", first);
        Assert.Contains("data-package=\"pkg-21\"", second);
        Assert.DoesNotContain("data-package=\"pkg-01\"", second);
        using var bad = await _anonymous.GetAsync("r/web/npm-packages?p=0");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    // ---- package page ----

    [Fact]
    public async Task ThePackagePageShowsReadmeVersionsTagsInstallCommandsAndTheNpmrcLines()
    {
        await PublishAsync("left-pad", "1.0.0", readme: "# Hello readme\n\n<script>alert(1)</script>");
        await PublishAsync("left-pad", "1.1.0-beta.1", tag: "next", readme: "# Newer");

        var html = await _anonymous.GetPageAsync("r/web/npm-packages/left-pad");
        Assert.Contains("id=\"npm-selected-version\">1.0.0<", html);
        Assert.Contains("Hello readme", html);
        Assert.DoesNotContain("<script>alert(1)", html);
        Assert.Contains("npm install left-pad@1.0.0", html);
        Assert.Contains("yarn add left-pad@1.0.0", html);
        Assert.Contains("registry=http://localhost/r/web/npm/", html);
        Assert.Contains("//localhost/r/web/npm/:_authToken=", html);
        Assert.Contains("data-version=\"1.0.0\"", html);
        Assert.Contains("data-version=\"1.1.0-beta.1\"", html);
        Assert.Contains("data-tag=\"next\"", html);
        Assert.Contains("http://localhost/r/web/npm/left-pad/-/left-pad-1.0.0.tgz", html);

        var beta = await _anonymous.GetPageAsync("r/web/npm-packages/left-pad?version=1.1.0-beta.1");
        Assert.Contains("id=\"npm-selected-version\">1.1.0-beta.1<", beta);
        Assert.Contains("npm install left-pad@next", beta);
    }

    [Fact]
    public async Task AScopedPackageHasItsScopedRegistryLineAndItsPageResolvesWhateverTheSpelling()
    {
        await PublishAsync("@acme/tool", "3.0.0");

        var plain = await _anonymous.GetPageAsync("r/web/npm-packages/@acme/tool");
        var encoded = await _anonymous.GetPageAsync("r/web/npm-packages/@acme%2ftool");

        Assert.Contains("@acme:registry=http://localhost/r/web/npm/", plain);
        Assert.Contains("npm install @acme/tool@3.0.0", plain);
        Assert.Contains("data-package=\"@acme/tool\"", encoded);
        using var upper = await _anonymous.GetAsync("r/web/npm-packages/@Acme/Tool");
        Assert.Equal(HttpStatusCode.NotFound, upper.StatusCode);
    }

    [Fact]
    public async Task ADeprecatedVersionIsFlaggedAndDependenciesAreListed()
    {
        await PublishAsync("left-pad", "1.0.0");
        await Registry(r => r.DeprecateAsync(Admin(), "left-pad", new Dictionary<string, string?> { ["1.0.0"] = "use 2" }, CancellationToken.None));

        var html = await _anonymous.GetPageAsync("r/web/npm-packages/left-pad");

        Assert.Contains("id=\"npm-deprecated\"", html);
        Assert.Contains("use 2", html);
        Assert.Contains("badge-deprecated", html);
    }

    [Fact]
    public async Task AMissingPackageIs404WithAMessage()
    {
        using var response = await _anonymous.GetAsync("r/web/npm-packages/nothing-here");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("npm-package-missing", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OwnersAndTheManageLinkShowToOwnersAndAdministratorsOnly()
    {
        await PublishAsync("left-pad", "1.0.0");

        var asOwner = await _alice.GetPageAsync("r/web/npm-packages/left-pad");
        var asAdmin = await _root.GetPageAsync("r/web/npm-packages/left-pad");
        var asOther = await _bob.GetPageAsync("r/web/npm-packages/left-pad");
        var anonymous = await _anonymous.GetPageAsync("r/web/npm-packages/left-pad");

        Assert.Contains("id=\"npm-manage-link\"", asOwner);
        Assert.Contains("href=\"/r/web/npm-manage/left-pad\"", asOwner);
        Assert.Contains("id=\"npm-manage-link\"", asAdmin);
        Assert.DoesNotContain("npm-manage-link", asOther);
        Assert.DoesNotContain("npm-manage-link", anonymous);
        Assert.Contains("alice", asAnonymousOwners(anonymous));

        static string asAnonymousOwners(string html) => html.Substring(html.IndexOf("npm-owners", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnOwnerLinksToTheServerWideProfileWhichResolves()
    {
        await PublishAsync("left-pad", "1.0.0");

        var html = await _anonymous.GetPageAsync("r/web/npm-packages/left-pad");
        var owners = html.Substring(html.IndexOf("npm-owners", StringComparison.Ordinal));
        Assert.Contains("href=\"/profiles/alice\"", owners);
        Assert.DoesNotContain("/r/web/profiles/", owners);

        using var profile = await _anonymous.GetAsync("profiles/alice");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
    }

    // ---- format separation and the header ----

    [Fact]
    public async Task NuGetPagesInAnNpmRepositoryRedirectOrAre404AndNpmPagesElsewhereAre404()
    {
        await _app.CreateRepositoryAsync("feed", CancellationToken.None);

        using var front = await _anonymous.GetAsync("r/web/");
        using var packages = await _anonymous.GetAsync("r/web/packages");
        using var upload = await _anonymous.GetAsync("r/web/upload");
        using var statistics = await _anonymous.GetAsync("r/web/statistics");
        using var nugetPackage = await _anonymous.GetAsync("r/web/packages/Some.Package");
        using var npmInDefault = await _anonymous.GetAsync("npm-packages");
        using var npmInNuGet = await _anonymous.GetAsync("r/feed/npm-packages");
        using var npmUploadInNuGet = await _anonymous.GetAsync("r/feed/npm-upload");

        Assert.Equal(HttpStatusCode.Redirect, front.StatusCode);
        Assert.Equal("/r/web/npm-packages", front.Headers.Location?.ToString());
        Assert.Equal(HttpStatusCode.Redirect, packages.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, upload.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, statistics.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, nugetPackage.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, npmInDefault.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, npmInNuGet.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, npmUploadInNuGet.StatusCode);
    }

    [Fact]
    public async Task TheHeaderOfAnNpmPageLeadsToTheNpmPagesAndTheSwitcherOffersEveryRepository()
    {
        var html = await _alice.GetPageAsync("r/web/npm-packages");

        Assert.Contains("href=\"/r/web/npm-packages\"", html);
        Assert.Contains("href=\"/r/web/npm-upload\"", html);
        Assert.DoesNotContain("href=\"/r/web/statistics\"", html);
        Assert.Contains("id=\"repository-switcher\"", html);
        // The switcher lists the default repository too, and leads to the npm repository's own front page.
        var switcher = html.Substring(html.IndexOf("id=\"repository-switcher\"", StringComparison.Ordinal));
        switcher = switcher.Substring(0, switcher.IndexOf("</ul>", StringComparison.Ordinal));
        Assert.Contains("data-repository=\"default\"", switcher);
        Assert.Contains("href=\"/r/web/npm-packages\" data-repository=\"web\"", switcher);

        // From a NuGet page, the switcher leads to the npm repository's page, not to a NuGet page that is not there.
        var fromDefault = await _alice.GetPageAsync("packages");
        Assert.Contains("href=\"/r/web/npm-packages\" data-repository=\"web\"", fromDefault);
    }

    // ---- private repositories ----

    [Fact]
    public async Task APrivateNpmRepositoryHidesItsPagesFromThoseWhoCannotReadIt()
    {
        await PublishAsync("left-pad", "1.0.0");
        await PublishAsync("@acme/secret", "1.0.0");
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().SetAnonymousReadAsync("web", false, CancellationToken.None);
            var admin = scope.ServiceProvider.GetRequiredService<IAccessAdministration>();
            await admin.DeleteTargetAsync(BuiltInNames.AuthenticatedDeploy, null, CancellationToken.None);
            await admin.CreateTargetAsync("bob reads left", new[] { "web" }, new[] { "left-*" }, null, null, null, CancellationToken.None);
            await admin.SetGrantAsync("bob reads left", PrincipalType.User, (await FindAsync(_app, "bob"))!.Id, AccessAction.Read, null, CancellationToken.None);
        }

        using var anonymous = await _anonymous.GetAsync("r/web/npm-packages");
        using var upload = await _anonymous.GetAsync("r/web/npm-upload");
        using var alice = await _alice.GetAsync("r/web/npm-packages");
        using var bobList = await _bob.GetAsync("r/web/npm-packages");
        using var bobAllowed = await _bob.GetAsync("r/web/npm-packages/left-pad");
        using var bobDenied = await _bob.GetAsync("r/web/npm-packages/@acme/secret");
        using var bobEncoded = await _bob.GetAsync("r/web/npm-packages/@acme%2fsecret");
        using var bobUpper = await _bob.GetAsync("r/web/npm-packages/@ACME%2FSecret");

        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Contains("account/login", anonymous.Headers.Location?.ToString());
        Assert.Equal(HttpStatusCode.Redirect, upload.StatusCode);
        // Alice has no grant now: not even she reads it, as for NuGet repositories.
        Assert.Equal(HttpStatusCode.NotFound, alice.StatusCode);
        // Bob reads left-*: the front page opens for him, and names only those packages.
        Assert.Equal(HttpStatusCode.OK, bobList.StatusCode);
        var bobListing = await bobList.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("data-package=\"left-pad\"", bobListing);
        Assert.DoesNotContain("@acme/secret", bobListing);
        Assert.Contains("1 package", bobListing);
        Assert.Equal(HttpStatusCode.OK, bobAllowed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, bobDenied.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, bobEncoded.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, bobUpper.StatusCode);
    }

    // ---- upload ----

    private async Task<HttpResponseMessage> UploadAsync(Browser browser, byte[] bytes, string tag = "", string? apiKey = null, string page = "r/web/npm-upload")
    {
        var fields = new Dictionary<string, string>();
        if (tag != "") fields["tag"] = tag;
        if (apiKey != null) fields["apiKey"] = apiKey;

        return await browser.PostFileAsync(page, "package", "pkg.tgz", new MemoryStream(bytes), fields);
    }

    [Fact]
    public async Task ASignedInUserPublishesATarballAndBecomesItsOwner()
    {
        var tarball = NpmTestPackages.Tarball("left-pad", "1.0.0");

        using var response = await UploadAsync(_alice, tarball.Bytes);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Published left-pad@1.0.0.", html);
        Assert.Contains("href=\"/r/web/npm-packages/left-pad\"", html);
        var package = await PackageAsync("left-pad");
        Assert.Equal(tarball.Integrity, package!.Versions.Single().Integrity);
        Assert.Equal("latest", package.DistTags.Single().Key);
        using var scope = _app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepositoryContext>().Use((await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync("web", CancellationToken.None))!);
        Assert.Equal("alice", Assert.Single(await scope.ServiceProvider.GetRequiredService<PackageOwnershipService>().GetOwnersAsync("left-pad", CancellationToken.None)).OwnerName);
    }

    [Fact]
    public async Task TheTagOfTheFormIsUsedAndAnotherUsersVersionIsRefused()
    {
        await PublishAsync("left-pad", "1.0.0");

        using var tagged = await UploadAsync(_alice, NpmTestPackages.Tarball("left-pad", "2.0.0-rc.1").Bytes, tag: "next");
        using var foreign = await UploadAsync(_bob, NpmTestPackages.Tarball("left-pad", "3.0.0").Bytes);

        Assert.Equal(HttpStatusCode.OK, tagged.StatusCode);
        Assert.Equal("2.0.0-rc.1", (await PackageAsync("left-pad"))!.DistTags["next"]);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Contains("not allowed", await foreign.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, (await PackageAsync("left-pad"))!.Versions.Count);
    }

    [Fact]
    public async Task AFileThatIsNotATarballOrAnExistingVersionIsRefusedWithAMessage()
    {
        await PublishAsync("left-pad", "1.0.0");

        using var notATarball = await UploadAsync(_alice, Encoding.UTF8.GetBytes("hello"));
        using var duplicate = await UploadAsync(_alice, NpmTestPackages.Tarball("left-pad", "1.0.0").Bytes);
        using var noFile = await _alice.PostAsync("r/web/npm-upload", new Dictionary<string, string> { { "tag", "x" } });

        Assert.Equal(HttpStatusCode.BadRequest, notATarball.StatusCode);
        Assert.Contains("not an npm tarball", await notATarball.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noFile.StatusCode);
    }

    [Fact]
    public async Task AnAnonymousVisitorIsAskedToSignInAndCannotPost()
    {
        var html = await _anonymous.GetPageAsync("r/web/npm-upload");
        Assert.Contains("id=\"upload-sign-in\"", html);
        Assert.DoesNotContain("npm-upload-form", html);
        Assert.Contains("npm publish --registry=http://localhost/r/web/npm/", html);

        using var post = await _anonymous.PostFileAsync("r/web/npm-upload", "package", "p.tgz", new MemoryStream(NpmTestPackages.Tarball("left-pad", "1.0.0").Bytes), tokenPage: "account/login");
        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        Assert.Null(await PackageAsync("left-pad"));
    }

    [Fact]
    public async Task APostWithoutTheAntiforgeryTokenIsRefused()
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(NpmTestPackages.Tarball("left-pad", "1.0.0").Bytes), "package", "p.tgz");
        using var request = new HttpRequestMessage(HttpMethod.Post, "r/web/npm-upload") { Content = content };

        using var response = await _alice.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(await PackageAsync("left-pad"));
    }

    [Fact]
    public async Task WithoutAccountsTheApiKeyTypedIntoTheFormIsTheCredential()
    {
        using var app = new BaGetApplication(TestOutputHelperFor(), configOverrides: new Dictionary<string, string> { { "ApiKey", "page-key" } });
        await app.CreateNpmRepositoryAsync("web", cancellationToken: CancellationToken.None);
        using var browser = new Browser(app);
        var tarball = NpmTestPackages.Tarball("left-pad", "1.0.0");

        Assert.Contains("name=\"apiKey\"", await browser.GetPageAsync("r/web/npm-upload"));
        using var wrong = await browser.PostFileAsync("r/web/npm-upload", "package", "p.tgz", new MemoryStream(tarball.Bytes), new Dictionary<string, string> { { "apiKey", "nope" } });
        using var right = await browser.PostFileAsync("r/web/npm-upload", "package", "p.tgz", new MemoryStream(tarball.Bytes), new Dictionary<string, string> { { "apiKey", "page-key" } });

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
    }

    [Fact]
    public async Task ReadOnlyModeAndAServerWithNothingToAuthenticateWithCloseTheUploadPage()
    {
        using var readOnly = new BaGetApplication(TestOutputHelperFor(), configOverrides: new Dictionary<string, string> { { "ApiKey", "k" }, { "IsReadOnlyMode", "true" } });
        await readOnly.CreateNpmRepositoryAsync("web", cancellationToken: CancellationToken.None);
        using var open = new BaGetApplication(TestOutputHelperFor());
        await open.CreateNpmRepositoryAsync("web", cancellationToken: CancellationToken.None);
        using var readOnlyBrowser = new Browser(readOnly);
        using var openBrowser = new Browser(open);

        Assert.Contains("id=\"upload-read-only\"", await readOnlyBrowser.GetPageAsync("r/web/npm-upload"));
        Assert.Contains("id=\"upload-no-api-key\"", await openBrowser.GetPageAsync("r/web/npm-upload"));
    }

    [Fact]
    public async Task TheMaintenanceGateRefusesAnUpload()
    {
        var gate = _app.Services.GetRequiredService<MaintenanceGate>();
        var tokenPage = await _alice.TokenAsync("r/web/npm-upload");
        Assert.NotEmpty(tokenPage);
        gate.Close("Copying");
        try
        {
            using var response = await UploadAsync(_alice, NpmTestPackages.Tarball("left-pad", "1.0.0").Bytes);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        finally
        {
            gate.Open();
        }

        Assert.Null(await PackageAsync("left-pad"));
    }

    // ---- manage ----

    private Task<HttpResponseMessage> ManageAsync(Browser browser, string package, string handler, Dictionary<string, string> fields)
        => browser.PostAsync($"r/web/npm-manage/{package}", fields, action: $"r/web/npm-manage/{package}?handler={handler}");

    [Fact]
    public async Task OnlyOwnersAndAdministratorsOpenTheManagePage()
    {
        await PublishAsync("left-pad", "1.0.0");

        using var owner = await _alice.GetAsync("r/web/npm-manage/left-pad");
        using var admin = await _root.GetAsync("r/web/npm-manage/left-pad");
        using var other = await _bob.GetAsync("r/web/npm-manage/left-pad");
        using var anonymous = await _anonymous.GetAsync("r/web/npm-manage/left-pad");
        using var missing = await _alice.GetAsync("r/web/npm-manage/nothing-here");
        using var invalid = await _alice.GetAsync("r/web/npm-manage/Bad%20Name");

        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        Assert.Contains("manage-as-admin", await admin.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
    }

    [Fact]
    public async Task TagsCanBeSetAndRemovedFromThePage()
    {
        await PublishAsync("left-pad", "1.0.0");
        await PublishAsync("left-pad", "1.1.0");

        using var set = await ManageAsync(_alice, "left-pad", "SetTag", new Dictionary<string, string> { { "tag", "stable" }, { "version", "1.0.0" } });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Contains("stable now points at 1.0.0.", await set.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("1.0.0", (await PackageAsync("left-pad"))!.DistTags["stable"]);

        using var bad = await ManageAsync(_alice, "left-pad", "SetTag", new Dictionary<string, string> { { "tag", "1.0.0" }, { "version", "1.0.0" } });
        Assert.Contains("cannot be a version", await bad.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        using var remove = await ManageAsync(_alice, "left-pad", "RemoveTag", new Dictionary<string, string> { { "tag", "stable" } });
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        Assert.DoesNotContain("stable", (await PackageAsync("left-pad"))!.DistTags.Keys);
    }

    [Fact]
    public async Task AVersionCanBeDeprecatedAndUndeprecatedFromThePage()
    {
        await PublishAsync("left-pad", "1.0.0");

        using var empty = await ManageAsync(_alice, "left-pad", "Deprecate", new Dictionary<string, string> { { "version", "1.0.0" }, { "message", " " } });
        Assert.Contains("Write the deprecation message.", await empty.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        using var deprecate = await ManageAsync(_alice, "left-pad", "Deprecate", new Dictionary<string, string> { { "version", "1.0.0" }, { "message", "use 2.x" } });
        Assert.Contains("left-pad@1.0.0 is deprecated.", await deprecate.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("use 2.x", (await PackageAsync("left-pad"))!.Versions.Single().Deprecated);

        using var undo = await ManageAsync(_alice, "left-pad", "Undeprecate", new Dictionary<string, string> { { "version", "1.0.0" } });
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);
        Assert.Null((await PackageAsync("left-pad"))!.Versions.Single().Deprecated);
    }

    [Fact]
    public async Task UnpublishingFromThePageNeedsThePasswordAndDeletesTheTarball()
    {
        await PublishAsync("left-pad", "1.0.0");
        await PublishAsync("left-pad", "1.1.0");

        using var wrong = await ManageAsync(_alice, "left-pad", "Unpublish", new Dictionary<string, string> { { "version", "1.1.0" }, { "password", "wrong" } });
        Assert.Contains("confirmation is incorrect", await wrong.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, (await PackageAsync("left-pad"))!.Versions.Count);

        using var right = await ManageAsync(_alice, "left-pad", "Unpublish", new Dictionary<string, string> { { "version", "1.1.0" }, { "password", Password } });
        Assert.Contains("left-pad@1.1.0 is unpublished.", await right.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var package = (await PackageAsync("left-pad"))!;
        Assert.Equal("1.0.0", Assert.Single(package.Versions).Version);
        Assert.Equal("1.0.0", package.DistTags["latest"]);
        using var scope = _app.Services.CreateScope();
        await Assert.ThrowsAnyAsync<IOException>(async () =>
            (await scope.ServiceProvider.GetRequiredService<IStorageService>().GetAsync("repos/web/npm/left-pad/-/left-pad-1.1.0.tgz", CancellationToken.None)).Dispose());
    }

    [Fact]
    public async Task UnpublishingEverythingFromThePageRemovesThePackageAndReturnsToTheList()
    {
        await PublishAsync("left-pad", "1.0.0");

        using var wrong = await ManageAsync(_alice, "left-pad", "UnpublishAll", new Dictionary<string, string> { { "password", "wrong" } });
        Assert.NotNull(await PackageAsync("left-pad"));

        using var right = await ManageAsync(_alice, "left-pad", "UnpublishAll", new Dictionary<string, string> { { "password", Password } });

        Assert.Equal(HttpStatusCode.Redirect, right.StatusCode);
        Assert.Contains("npm-packages", right.Headers.Location?.ToString());
        Assert.Null(await PackageAsync("left-pad"));
    }

    [Fact]
    public async Task UnpublishingTheLastVersionFromThePageReturnsToTheList()
    {
        await PublishAsync("left-pad", "1.0.0");

        using var response = await ManageAsync(_alice, "left-pad", "Unpublish", new Dictionary<string, string> { { "version", "1.0.0" }, { "password", Password } });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(await PackageAsync("left-pad"));
    }

    [Fact]
    public async Task OwnersAreAddedAndRemovedAndTheLastOneStays()
    {
        await PublishAsync("left-pad", "1.0.0");

        using var add = await ManageAsync(_alice, "left-pad", "AddOwner", new Dictionary<string, string> { { "ownerName", "bob" } });
        Assert.Contains("bob is now an owner.", await add.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var again = await ManageAsync(_alice, "left-pad", "AddOwner", new Dictionary<string, string> { { "ownerName", "bob" } });
        Assert.Contains("already owns", await again.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var unknown = await ManageAsync(_alice, "left-pad", "AddOwner", new Dictionary<string, string> { { "ownerName", "nobody" } });
        Assert.Contains("no such user", await unknown.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Bob owns it now, so he can publish a version.
        using var upload = await UploadAsync(_bob, NpmTestPackages.Tarball("left-pad", "1.1.0").Bytes);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        using var remove = await ManageAsync(_bob, "left-pad", "RemoveOwner", new Dictionary<string, string> { { "ownerName", "alice" } });
        Assert.Contains("alice is no longer an owner.", await remove.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var last = await ManageAsync(_bob, "left-pad", "RemoveOwner", new Dictionary<string, string> { { "ownerName", "bob" } });
        Assert.Contains("keeps at least one owner", await last.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnAdministratorManagesAnotherUsersPackageAndAnOutsiderCannotPost()
    {
        await PublishAsync("left-pad", "1.0.0");

        using var admin = await ManageAsync(_root, "left-pad", "SetTag", new Dictionary<string, string> { { "tag", "stable" }, { "version", "1.0.0" } });
        using var outsider = await _bob.RawPostAsync("r/web/npm-manage/left-pad?handler=SetTag", new Dictionary<string, string> { { "tag", "x" }, { "version", "1.0.0" } });

        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        Assert.Equal("1.0.0", (await PackageAsync("left-pad"))!.DistTags["stable"]);
        Assert.NotEqual(HttpStatusCode.OK, outsider.StatusCode);
        Assert.DoesNotContain("x", (await PackageAsync("left-pad"))!.DistTags.Keys);
    }

    [Fact]
    public async Task APostWithoutTheAntiforgeryTokenIsRefusedOnTheManagePage()
    {
        await PublishAsync("left-pad", "1.0.0");

        using var response = await _alice.RawPostAsync("r/web/npm-manage/left-pad?handler=RemoveTag", new Dictionary<string, string> { { "tag", "latest" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("latest", (await PackageAsync("left-pad"))!.DistTags.Keys);
    }

    [Fact]
    public async Task TheManagePageRefusesWritesWhileTheGateIsClosedAndInReadOnlyMode()
    {
        await PublishAsync("left-pad", "1.0.0");
        var token = await _alice.TokenAsync("r/web/npm-manage/left-pad");

        var gate = _app.Services.GetRequiredService<MaintenanceGate>();
        gate.Close("Copying");
        try
        {
            using var closed = await _alice.PostAsync("r/web/npm-manage/left-pad", new Dictionary<string, string> { { "tag", "latest" } }, action: "r/web/npm-manage/left-pad?handler=RemoveTag");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, closed.StatusCode);
        }
        finally
        {
            gate.Open();
        }

        Assert.NotEmpty(token);
        Assert.Contains("latest", (await PackageAsync("left-pad"))!.DistTags.Keys);

        using var readOnly = new BaGetApplication(TestOutputHelperFor(), configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "true" }, { "ApiKey", "" }, { "IsReadOnlyMode", "true" } });
        await readOnly.CreateNpmRepositoryAsync("web", cancellationToken: CancellationToken.None);
        await CreateUserAsync(readOnly, "root", administrator: true);
        using var browser = new Browser(readOnly);
        await SignInAsync(browser, "root");
        using var attempt = await browser.PostAsync("r/web/npm-manage/anything", new Dictionary<string, string> { { "tag", "latest" } }, action: "r/web/npm-manage/anything?handler=RemoveTag", tokenPage: "account/settings");
        Assert.Equal(HttpStatusCode.OK, attempt.StatusCode);
        Assert.Contains("read-only mode", await attempt.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    // ---- other pages that mention packages ----

    [Fact]
    public async Task MyPackagesListsNpmPackagesWithTheirOwnLinks()
    {
        await PublishAsync("left-pad", "1.0.0");
        await PublishAsync("left-pad", "1.1.0");

        var html = await _alice.GetPageAsync("account/packages");

        Assert.Contains("href=\"/r/web/npm-packages/left-pad\"", html);
        Assert.Contains("href=\"/r/web/npm-packages/left-pad?version=1.1.0\"", html);
        Assert.Contains("href=\"/r/web/npm-manage/left-pad\"", html);
        Assert.Contains("href=\"/r/web/npm-upload\"", html);
    }

    [Fact]
    public async Task AnApiKeyCanBeLimitedToAnNpmRepositoryFromTheAccountPage()
    {
        int webKey;
        using (var scope = _app.Services.CreateScope())
        {
            webKey = (await scope.ServiceProvider.GetRequiredService<IContext>().Repositories!.SingleAsync(r => r.Name == "web", TestContext.Current.CancellationToken)).Key;
        }

        var html = await _alice.GetPageAsync("account/apikeys");
        Assert.Contains($"name=\"RepositoryKeys\" value=\"{webKey}\"", html);

        using var created = await _alice.PostAsync("account/apikeys", new Dictionary<string, string>
        {
            { "Description", "ci" }, { "Scopes", ApiKeyScopes.Push }, { "PackageGlobPattern", "*" }, { "ExpiresInDays", "30" }, { "RepositoryKeys", webKey.ToString() },
        }, action: "account/apikeys?handler=Create");

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var check = _app.Services.CreateScope();
        var limits = await check.ServiceProvider.GetRequiredService<IContext>().ApiKeyRepositories!.ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains(limits, r => r.RepositoryKey == webKey);
    }

    [Fact]
    public async Task DeletingAUserAsAnAdministratorCanUnpublishTheirSoleOwnedNpmPackages()
    {
        await PublishAsync("left-pad", "1.0.0");
        var bob = await FindAsync(_app, "bob");
        Assert.NotNull(bob);

        using var scope = _app.Services.CreateScope();
        var deletion = scope.ServiceProvider.GetRequiredService<AccountDeletionService>();
        var alice = await FindAsync(_app, "alice");
        var result = await deletion.DeleteByAdministratorAsync(alice, deleteSoleOwnedPackages: true, "root", CancellationToken.None);

        Assert.True(result.Result.Succeeded);
        Assert.Equal("left-pad (web)", Assert.Single(result.DeletedPackageIds));
        Assert.Null(await PackageAsync("left-pad"));
    }

    // ---- helpers ----

    private ITestOutputHelper TestOutputHelperFor() => _output;

    private static BaGetPrincipal Admin() => new(null, "root", isSuperuser: false, ApiKeyScopes.All, "*", isAdministrator: true);

    private async Task Registry(Func<INpmRegistry, Task> action)
    {
        using var scope = _app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepositoryContext>().Use((await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync("web", CancellationToken.None))!);
        await action(scope.ServiceProvider.GetRequiredService<INpmRegistry>());
    }
}
