using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using static BaGet.Tests.AdminTestSupport;

namespace BaGet.Tests;

/// <summary>
/// The administrator's pages for remote repositories: create from the Repositories page, edit the upstream, test the
/// connection with unsaved values, see the cache, purge it (whole or one package) and browse what is cached.
/// </summary>
public class AdminRemoteRepositoriesTests : IDisposable
{
    private const string Url = "http://localhost/v3/index.json";

    private readonly BaGetApplication _upstream;
    private readonly BaGetApplication _app;
    private readonly ForwardingRemoteHttpClientFactory _wire;
    private readonly Browser _admin;

    public AdminRemoteRepositoriesTests(ITestOutputHelper output)
    {
        _upstream = new BaGetApplication(output);
        _wire = new ForwardingRemoteHttpClientFactory(_upstream.CreateClient());
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string>
            {
                { "Accounts:Enabled", "true" },
                { "ApiKey", "" },
            },
            configureServices: services => services.AddSingleton<IRemoteHttpClientFactory>(_wire));
        _admin = new Browser(_app);
        CreateUserAsync(_app, "root", administrator: true).GetAwaiter().GetResult();
        SignInAsync(_admin, "root").GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _admin.Dispose();
        _app.Dispose();
        _upstream.Dispose();
    }

    private static Dictionary<string, string> Form(string name, string url = Url, Action<Dictionary<string, string>>? change = null)
    {
        var fields = new Dictionary<string, string>
        {
            { "name", name },
            { "password", Password },
            { "Remote.Url", url },
            { "Remote.Protocol", "V3" },
            { "Remote.Username", "" },
            { "Remote.Password", "" },
            { "Remote.MetadataTtlSeconds", "600" },
            { "Remote.NegativeTtlSeconds", "60" },
            { "Remote.DownloadTimeoutSeconds", "600" },
            { "Remote.StoreLocally", "true" },
            { "Remote.Offline", "false" },
        };
        change?.Invoke(fields);

        return fields;
    }

    private Task<HttpResponseMessage> CreateAsync(string name, string url = Url, Action<Dictionary<string, string>>? change = null)
        => _admin.PostAsync("admin/repositories", Form(name, url, change), action: "admin/repositories?handler=Remote");

    private async Task<RemoteRepositoryDetail?> FindAsync(string name)
    {
        using var scope = _app.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().FindAsync(name, CancellationToken.None);
    }

    private async Task SeedUpstreamAsync()
    {
        using var package = TestResources.GetResourceStream(TestResources.Package);
        await _upstream.AddPackageAsync(package, CancellationToken.None);
    }

    private async Task<string> ReadAsync(HttpResponseMessage response)
    {
        using (response)
        {
            return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        }
    }

    // A post that redirects back to the page with a message: the page after it.
    private async Task<string> PostThenReadAsync(string action, Dictionary<string, string> fields)
    {
        using (var response = await _admin.PostAsync("admin/repositories/up", fields, action: action))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        return await _admin.GetPageAsync("admin/repositories/up");
    }

    [Fact]
    public async Task OnlyAdministratorsCanCreateOrEditARemoteRepository()
    {
        await CreateUserAsync(_app, "alice");
        using var alice = new Browser(_app);
        await SignInAsync(alice, "alice");

        using var create = await alice.RawPostAsync("admin/repositories?handler=Remote", Form("up"));
        Assert.NotEqual(HttpStatusCode.OK, create.StatusCode);
        Assert.Null(await FindAsync("up"));

        (await CreateAsync("up")).Dispose();
        foreach (var handler in new[] { "", "?handler=Test", "?handler=Purge", "?handler=PurgePackage" })
        {
            using var post = await alice.RawPostAsync("admin/repositories/up" + handler, new Dictionary<string, string>());
            Assert.NotEqual(HttpStatusCode.OK, post.StatusCode);
        }
    }

    [Fact]
    public async Task CreatingARemoteRepositoryListsItAndOpensItsPage()
    {
        using var created = await CreateAsync("up", change: f =>
        {
            f["Remote.Username"] = "bob";
            f["Remote.Password"] = "s3cret-token";
        });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.EndsWith("/admin/repositories/up", created.Headers.Location!.OriginalString);

        var detail = await FindAsync("up");
        Assert.Equal(Url, detail!.Settings.Url);
        Assert.Equal("bob", detail.Settings.Username);
        Assert.True(detail.HasPassword);

        var list = await _admin.GetPageAsync("admin/repositories");
        Assert.Contains("data-repository=\"up\"", list);
        Assert.Contains("NuGet / Remote", list);
        Assert.Contains("/r/up/v3/index.json", list);

        Assert.Contains("Remote repository up was created.", list);

        var page = await _admin.GetPageAsync("admin/repositories/up");
        Assert.Contains(Url, page);
        Assert.Contains("value=\"bob\"", page);
        Assert.Contains("A password is stored", page);
        Assert.DoesNotContain("s3cret-token", page);
        Assert.Contains("id=\"remote-test\"", page);
        Assert.Contains("id=\"remote-purge-form\"", page);
        Assert.Contains("id=\"remote-browse\"", page);
        Assert.Contains("remote-insecure", page);
        Assert.Contains("a remote repository takes no deployments", page);
    }

    [Fact]
    public async Task InvalidUpstreamSettingsAreRefusedWithAMessageAndNothingIsCreated()
    {
        foreach (var (url, expected) in new[] { ("", "required"), ("ftp://host/x", "http or https"), ("http://u:p@host/x", "not in the URL") })
        {
            var html = await ReadAsync(await CreateAsync("up", url));

            Assert.Contains("id=\"repositories-error\"", html);
            Assert.Contains(expected, html);
            Assert.Null(await FindAsync("up"));
        }

        var negative = await ReadAsync(await CreateAsync("up", change: f => f["Remote.MetadataTtlSeconds"] = "-5"));
        Assert.Contains("metadata cache period", negative);

        var name = await ReadAsync(await CreateAsync("Not Valid"));
        Assert.Contains("lowercase", name);
    }

    [Fact]
    public async Task EditingSavesTheUpstreamAndABlankPasswordKeepsTheStoredOne()
    {
        (await CreateAsync("up", change: f =>
        {
            f["Remote.Username"] = "bob";
            f["Remote.Password"] = "pw";
        })).Dispose();

        using var saved = await _admin.PostAsync("admin/repositories/up", new Dictionary<string, string>
        {
            { "anonymousRead", "true" },
            { "password", Password },
            { "Remote.Url", "http://localhost/other/index.json" },
            { "Remote.Protocol", "V3" },
            { "Remote.Username", "bob" },
            { "Remote.Password", "" },
            { "Remote.MetadataTtlSeconds", "30" },
            { "Remote.NegativeTtlSeconds", "5" },
            { "Remote.DownloadTimeoutSeconds", "90" },
            { "Remote.StoreLocally", "false" },
            { "Remote.Offline", "true" },
        });
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        var detail = await FindAsync("up");
        Assert.Equal("http://localhost/other/index.json", detail!.Settings.Url);
        Assert.Equal(30, detail.Settings.MetadataTtlSeconds);
        Assert.Equal(5, detail.Settings.NegativeTtlSeconds);
        Assert.Equal(90, detail.Settings.DownloadTimeoutSeconds);
        Assert.False(detail.Settings.StoreLocally);
        Assert.True(detail.Settings.Offline);
        Assert.True(detail.HasPassword);

        using var cleared = await _admin.PostAsync("admin/repositories/up", new Dictionary<string, string>
        {
            { "anonymousRead", "true" },
            { "password", Password },
            { "Remote.Url", Url },
            { "Remote.Protocol", "V3" },
            { "Remote.Username", "" },
            { "Remote.Password", "" },
            { "Remote.ClearPassword", "true" },
            { "Remote.MetadataTtlSeconds", "30" },
            { "Remote.NegativeTtlSeconds", "5" },
            { "Remote.DownloadTimeoutSeconds", "90" },
            { "Remote.StoreLocally", "true" },
            { "Remote.Offline", "false" },
        });
        Assert.Equal(HttpStatusCode.Redirect, cleared.StatusCode);
        Assert.False((await FindAsync("up"))!.HasPassword);
    }

    [Fact]
    public async Task AnInvalidEditIsRefusedWithAMessageAndKeepsWhatWasSaved()
    {
        (await CreateAsync("up")).Dispose();

        var html = await ReadAsync(await _admin.PostAsync("admin/repositories/up", new Dictionary<string, string>
        {
            { "anonymousRead", "true" },
            { "password", Password },
            { "Remote.Url", "not a url" },
            { "Remote.Protocol", "V3" },
            { "Remote.MetadataTtlSeconds", "600" },
            { "Remote.NegativeTtlSeconds", "60" },
            { "Remote.DownloadTimeoutSeconds", "600" },
        }));

        Assert.Contains("id=\"repository-error\"", html);
        Assert.Contains("http or https", html);
        Assert.Equal(Url, (await FindAsync("up"))!.Settings.Url);
    }

    [Fact]
    public async Task TestConnectionUsesTheUnsavedValuesAndSavesNothing()
    {
        (await CreateAsync("up")).Dispose();

        var good = await ReadAsync(await _admin.PostAsync("admin/repositories/up", new Dictionary<string, string>
        {
            { "password", Password },
            { "Remote.Url", Url },
            { "Remote.Protocol", "V3" },
            { "Remote.MetadataTtlSeconds", "600" },
            { "Remote.NegativeTtlSeconds", "60" },
            { "Remote.DownloadTimeoutSeconds", "600" },
        }, action: "admin/repositories/up?handler=Test"));
        Assert.Contains("id=\"remote-test-result\"", good);
        Assert.Contains("alert-success", good);
        Assert.Contains("V3 service index", good);

        var bad = await ReadAsync(await _admin.PostAsync("admin/repositories/up", new Dictionary<string, string>
        {
            { "password", Password },
            { "Remote.Url", "http://localhost/nothing/here" },
            { "Remote.Protocol", "V3" },
            { "Remote.MetadataTtlSeconds", "600" },
            { "Remote.NegativeTtlSeconds", "60" },
            { "Remote.DownloadTimeoutSeconds", "600" },
        }, action: "admin/repositories/up?handler=Test"));
        Assert.Contains("alert-danger", bad);
        Assert.Contains("HTTP 404", bad);
        Assert.Contains("http://localhost/nothing/here", bad);

        Assert.Equal(Url, (await FindAsync("up"))!.Settings.Url);
    }

    [Fact]
    public async Task TheCacheSectionShowsWhatIsStoredAndPurgeEmptiesIt()
    {
        await SeedUpstreamAsync();
        (await CreateAsync("up")).Dispose();

        using var client = _app.CreateClient();
        using (var download = await client.GetAsync("r/up/v3/package/testdata/1.2.3/testdata.1.2.3.nupkg", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        }

        var page = await _admin.GetPageAsync("admin/repositories/up");
        Assert.Contains("1 version(s) of 1 package(s)", page);
        Assert.Contains("Answered at", page);

        // Purging one package that is not there says so; the right one deletes it.
        var nothing = await PostThenReadAsync("admin/repositories/up?handler=PurgePackage", new Dictionary<string, string>
        {
            { "password", Password },
            { "packageId", "Nope" },
            { "packageVersion", "" },
        });
        Assert.Contains("Nothing was cached for Nope", nothing);

        var invalidVersion = await ReadAsync(await _admin.PostAsync("admin/repositories/up", new Dictionary<string, string>
        {
            { "password", Password },
            { "packageId", "TestData" },
            { "packageVersion", "not-a-version" },
        }, action: "admin/repositories/up?handler=PurgePackage"));
        Assert.Contains("is not a valid version", invalidVersion);

        var noId = await ReadAsync(await _admin.PostAsync("admin/repositories/up", new Dictionary<string, string>
        {
            { "password", Password },
            { "packageId", " " },
            { "packageVersion", "" },
        }, action: "admin/repositories/up?handler=PurgePackage"));
        Assert.Contains("Enter the id", noId);

        var one = await PostThenReadAsync("admin/repositories/up?handler=PurgePackage", new Dictionary<string, string>
        {
            { "password", Password },
            { "packageId", "TestData" },
            { "packageVersion", "1.2.3" },
        });
        Assert.Contains("TestData 1.2.3 was purged: 1 package version(s) deleted", one);
        Assert.Contains("0 version(s) of 0 package(s)", one);

        // Fetch again, then purge it all.
        using (var again = await client.GetAsync("r/up/v3/package/testdata/1.2.3/testdata.1.2.3.nupkg", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        }

        var purged = await PostThenReadAsync("admin/repositories/up?handler=Purge", new Dictionary<string, string> { { "password", Password } });
        Assert.Contains("The cache of up was purged: 1 package version(s) deleted", purged);
        Assert.Contains("0 version(s) of 0 package(s)", purged);
    }

    [Fact]
    public async Task TheDeletePageOfARemoteRepositoryWithCachedPackagesSaysToPurgeFirst()
    {
        await SeedUpstreamAsync();
        (await CreateAsync("up")).Dispose();
        using (var client = _app.CreateClient())
        {
            (await client.GetAsync("r/up/v3/package/testdata/1.2.3/testdata.1.2.3.nupkg", TestContext.Current.CancellationToken)).Dispose();
        }

        var page = await _admin.GetPageAsync("admin/repositories/up/delete");
        Assert.Contains("id=\"delete-repository-not-empty\"", page);
        Assert.Contains("Purge its cache", page);
        Assert.Contains("/admin/repositories/up", page);

        (await _admin.PostAsync("admin/repositories/up", new Dictionary<string, string> { { "password", Password } }, action: "admin/repositories/up?handler=Purge")).Dispose();

        using var deleted = await _admin.PostAsync("admin/repositories/up/delete", new Dictionary<string, string>(), tokenPage: "admin/repositories");
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.Null(await FindAsync("up"));
    }

    [Fact]
    public async Task WritesAreRefusedWhileTheServerIsReadOnlyForMaintenance()
    {
        (await CreateAsync("up")).Dispose();
        var gate = _app.Services.GetRequiredService<MaintenanceGate>();
        gate.CloseUntilRestart("test");

        using var purge = await _admin.PostAsync("admin/repositories", new Dictionary<string, string> { { "password", Password } }, action: "admin/repositories/up?handler=Purge", tokenPage: "admin/repositories/up");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, purge.StatusCode);

        using var create = await CreateAsync("other");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, create.StatusCode);
        Assert.Null(await FindAsync("other"));
    }

    [Fact]
    public async Task ChangesPurgesAndTestsAskForThePasswordAgain()
    {
        await SeedUpstreamAsync();
        using var wrong = await CreateAsync("up", change: f => f["password"] = "not-my-password");
        var html = await ReadAsync(wrong);
        Assert.Contains("The confirmation is incorrect", html);
        Assert.Null(await FindAsync("up"));

        (await CreateAsync("up")).Dispose();
        using (var client = _app.CreateClient())
        {
            (await client.GetAsync("r/up/v3/package/testdata/1.2.3/testdata.1.2.3.nupkg", TestContext.Current.CancellationToken)).Dispose();
        }

        var save = EditFields("password", "wrong");
        save["Remote.Url"] = "http://localhost/changed/index.json";
        Assert.Contains("The confirmation is incorrect", await ReadAsync(await _admin.PostAsync("admin/repositories/up", save)));
        Assert.Equal(Url, (await FindAsync("up"))!.Settings.Url);

        foreach (var handler in new[] { "Purge", "PurgePackage" })
        {
            var refused = await ReadAsync(await _admin.PostAsync(
                "admin/repositories/up",
                new Dictionary<string, string> { { "password", "wrong" }, { "packageId", "TestData" } },
                action: "admin/repositories/up?handler=" + handler));
            Assert.Contains("The confirmation is incorrect", refused);
        }

        using (var scope = _app.Services.CreateScope())
        {
            Assert.Equal(1, (await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().FindAsync("up", CancellationToken.None))!.PackageCount);
        }

        // Testing saves nothing but makes the server send a request to a typed URL, so it asks too.
        var wireBefore = _wire.Requests.Length;
        var refusedTest = await ReadAsync(await _admin.PostAsync("admin/repositories/up", EditFields("password", "wrong"), action: "admin/repositories/up?handler=Test"));
        Assert.Contains("The confirmation is incorrect", refusedTest);
        Assert.DoesNotContain("id=\"remote-test-result\"", refusedTest);
        Assert.Equal(wireBefore, _wire.Requests.Length);

        // The wrong passwords above counted as failed attempts and locked the account out (as a sign-in would); lift it to try the right one.
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<BaGet.Core.BaGetUser>>();
            var root = (await users.FindByNameAsync("root"))!;
            await users.SetLockoutEndDateAsync(root, null);
            await users.ResetAccessFailedCountAsync(root);
        }

        var test = EditFields(null, null);
        Assert.Contains("id=\"remote-test-result\"", await ReadAsync(await _admin.PostAsync("admin/repositories/up", test, action: "admin/repositories/up?handler=Test")));
    }

    [Fact]
    public async Task TheStoredPasswordIsNotSentToAnotherUrlWhileTesting()
    {
        (await CreateAsync("up", change: f =>
        {
            f["Remote.Username"] = "bob";
            f["Remote.Password"] = "stored-pw";
        })).Dispose();

        var fields = EditFields(null, null);
        fields["Remote.Username"] = "bob";
        fields["Remote.Url"] = "http://other.example/elsewhere/index.json";
        var html = await ReadAsync(await _admin.PostAsync("admin/repositories/up", fields, action: "admin/repositories/up?handler=Test"));

        var withStored = "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("bob:stored-pw"));
        Assert.Contains("alert-danger", html);
        Assert.All(_wire.Requests, r => Assert.NotEqual(withStored, r.Authorization));

        // The same URL and user name do use it.
        fields["Remote.Url"] = Url;
        await ReadAsync(await _admin.PostAsync("admin/repositories/up", fields, action: "admin/repositories/up?handler=Test"));
        Assert.Contains(_wire.Requests, r => r.Authorization == withStored);
    }

    [Fact]
    public async Task TestingWithAnotherPathOnTheSameOriginUsesTheStoredPasswordLikeSaving()
    {
        (await CreateAsync("up", change: f =>
        {
            f["Remote.Username"] = "bob";
            f["Remote.Password"] = "stored-pw";
        })).Dispose();

        var fields = EditFields(null, null);
        fields["Remote.Username"] = "bob";
        fields["Remote.Url"] = "http://localhost/other/path/index.json";
        await ReadAsync(await _admin.PostAsync("admin/repositories/up", fields, action: "admin/repositories/up?handler=Test"));

        var withStored = "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("bob:stored-pw"));
        Assert.Contains(_wire.Requests, r => r.Uri.AbsolutePath == "/other/path/index.json" && r.Authorization == withStored);
    }

    private static Dictionary<string, string> EditFields(string? key, string? value)
    {
        var fields = new Dictionary<string, string>
        {
            { "anonymousRead", "true" },
            { "password", Password },
            { "Remote.Url", Url },
            { "Remote.Protocol", "V3" },
            { "Remote.MetadataTtlSeconds", "600" },
            { "Remote.NegativeTtlSeconds", "60" },
            { "Remote.DownloadTimeoutSeconds", "600" },
        };
        if (key != null) fields[key] = value!;

        return fields;
    }

    private async Task<bool> AnonymousReadAsync(string name)
    {
        using var scope = _app.Services.CreateScope();

        return (await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().FindAsync(name, CancellationToken.None))!.AnonymousRead;
    }

    [Fact]
    public async Task ARemoteRepositoryWithCredentialsIsNotAnonymouslyReadableUnlessAskedAndOneWithoutIsByDefault()
    {
        (await CreateAsync("plain")).Dispose();
        (await CreateAsync("secret", change: f =>
        {
            f["Remote.Username"] = "bob";
            f["Remote.Password"] = "pw";
        })).Dispose();
        (await CreateAsync("open", change: f =>
        {
            f["Remote.Username"] = "bob";
            f["Remote.Password"] = "pw";
            f["anonymousRead"] = "true";
        })).Dispose();
        (await CreateAsync("closed", change: f => f["anonymousRead"] = "false")).Dispose();

        Assert.True(await AnonymousReadAsync("plain"));
        Assert.False(await AnonymousReadAsync("secret"));
        Assert.True(await AnonymousReadAsync("open"));
        Assert.False(await AnonymousReadAsync("closed"));

        // The create form offers the choice; an anonymously readable repository with a stored password is flagged.
        var list = await _admin.GetPageAsync("admin/repositories");
        Assert.Contains("id=\"remote-anonymous-read\"", list);
        Assert.Contains("id=\"remote-anonymous-credentials\"", await _admin.GetPageAsync("admin/repositories/open"));
        Assert.DoesNotContain("id=\"remote-anonymous-credentials\"", await _admin.GetPageAsync("admin/repositories/secret"));
    }

    [Fact]
    public async Task ThePasswordlessServiceDefaultAlsoKeepsACredentialedRemoteRepositoryClosed()
    {
        using var scope = _app.Services.CreateScope();
        var repositories = scope.ServiceProvider.GetRequiredService<IRepositoryService>();

        await repositories.CreateAsync("svc", RepositoryFormat.NuGet, RepositoryKind.Remote, new RemoteRepositorySettings { Url = Url, Username = "bob" }, CancellationToken.None);
        await repositories.CreateAsync("svc2", RepositoryFormat.NuGet, RepositoryKind.Remote, new RemoteRepositorySettings { Url = Url }, CancellationToken.None);

        Assert.False(await AnonymousReadAsync("svc"));
        Assert.True(await AnonymousReadAsync("svc2"));
    }

    [Fact]
    public async Task APassThroughRepositoryAndAnOfflineOneSayWhatTheyDo()
    {
        (await CreateAsync("up", change: f =>
        {
            f["Remote.StoreLocally"] = "false";
            f["Remote.Offline"] = "true";
        })).Dispose();

        var page = await _admin.GetPageAsync("admin/repositories/up");

        Assert.Contains("Offline mode: never contacted.", page);
        Assert.Contains("Store artifacts locally", page);
        Assert.Contains("id=\"remote-test\"", page);
    }
}

/// <summary>
/// The default repository and <c>Mirror:*</c>: the mirror is a remote repository the default one fetches through.
/// </summary>
public class MirrorRepositoryTests : IDisposable
{
    private readonly BaGetApplication _upstream;
    private readonly BaGetApplication _app;
    private readonly Browser _admin;

    public MirrorRepositoryTests(ITestOutputHelper output)
    {
        _upstream = new BaGetApplication(output);
        _app = new BaGetApplication(
            output,
            _upstream.CreateClient(),
            new Dictionary<string, string> { { "Accounts:Enabled", "true" }, { "ApiKey", "" } });
        _admin = new Browser(_app);
        CreateUserAsync(_app, "root", administrator: true).GetAwaiter().GetResult();
        SignInAsync(_admin, "root").GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _admin.Dispose();
        _app.Dispose();
        _upstream.Dispose();
    }

    [Fact]
    public async Task AnNpmRemoteRepositoryNamedMirrorIsNeverTakenForTheMirror()
    {
        using var scope = _app.Services.CreateScope();
        var repositories = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
        var administration = scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>();

        // Replace the NuGet mirror created at startup by an npm repository of the same name.
        await administration.DeleteAsync(Repository.MirrorName, CancellationToken.None);
        await repositories.CreateAsync(
            Repository.MirrorName,
            RepositoryFormat.Npm,
            RepositoryKind.Remote,
            new RemoteRepositorySettings { Url = "http://registry.test/" },
            anonymousRead: true,
            CancellationToken.None);

        var remotes = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();
        await remotes.EnsureMirrorRepositoryAsync(CancellationToken.None);

        var detail = (await remotes.FindAsync(Repository.MirrorName, CancellationToken.None))!;
        Assert.False(detail.IsMirror);
        Assert.Equal(RemoteProtocol.Npm, detail.Settings.Protocol);
        Assert.Equal("http://registry.test/", detail.Settings.Url);

        // The default repository then reads Mirror:* directly rather than through that repository.
        var upstream = await scope.ServiceProvider.GetRequiredService<IRemoteUpstreamSource>().ResolveAsync(RepositoryInfo.Default, CancellationToken.None);
        Assert.Equal(0, upstream!.Key);
        Assert.Equal("http://localhost/v3/index.json", upstream.Settings.Url);
    }

    [Fact]
    public async Task EnablingTheMirrorCreatesARemoteRepositoryFromTheMirrorSettings()
    {
        using var scope = _app.Services.CreateScope();
        var detail = await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().FindAsync(Repository.MirrorName, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.True(detail!.IsMirror);

        // The auto-created mirror is anonymously readable like the default repository it serves (documented).
        Assert.True((await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().FindAsync(Repository.MirrorName, CancellationToken.None))!.AnonymousRead);
        Assert.Equal("http://localhost/v3/index.json", detail.Settings.Url);
        Assert.Equal(RemoteProtocol.V3, detail.Settings.Protocol);
        Assert.Equal(0, detail.Settings.MetadataTtlSeconds);
        Assert.Equal(0, detail.Settings.NegativeTtlSeconds);
        Assert.True(detail.Settings.StoreLocally);
        Assert.False(detail.Settings.Offline);
    }

    [Fact]
    public async Task TheMirrorRepositoryLocksWhatMirrorSettingsOwnAndTheDefaultRepositoryPointsToIt()
    {
        var mirror = await _admin.GetPageAsync("admin/repositories/mirror");
        Assert.Contains("readonly", mirror);
        Assert.Contains("Mirror:PackageSource", mirror);

        var home = await _admin.GetPageAsync("admin/repositories/default");
        Assert.Contains("id=\"repository-mirror-note\"", home);
        Assert.Contains("/admin/repositories/mirror", home);

        // The URL cannot be changed through the repository while Mirror:* owns it.
        using var saved = await _admin.PostAsync("admin/repositories/mirror", new Dictionary<string, string>
        {
            { "anonymousRead", "true" },
            { "password", Password },
            { "Remote.Url", "http://elsewhere/index.json" },
            { "Remote.Protocol", "V2" },
            { "Remote.MetadataTtlSeconds", "0" },
            { "Remote.NegativeTtlSeconds", "0" },
            { "Remote.DownloadTimeoutSeconds", "5" },
        });
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        using var scope = _app.Services.CreateScope();
        var detail = await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().FindAsync(Repository.MirrorName, CancellationToken.None);
        Assert.Equal("http://localhost/v3/index.json", detail!.Settings.Url);
        Assert.Equal(RemoteProtocol.V3, detail.Settings.Protocol);
    }

    [Fact]
    public async Task TheDefaultRepositoryStillStoresMirroredPackagesInItself()
    {
        using (var package = TestResources.GetResourceStream(TestResources.Package))
        {
            await _upstream.AddPackageAsync(package, CancellationToken.None);
        }

        using var client = _app.CreateClient();
        using var response = await client.GetAsync("v3/package/testdata/1.2.3/testdata.1.2.3.nupkg", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _app.Services.CreateScope();
        var repositories = scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>();
        Assert.Equal(1, (await repositories.FindAsync("default", CancellationToken.None))!.PackageCount);
        Assert.Equal(0, (await repositories.FindAsync(Repository.MirrorName, CancellationToken.None))!.PackageCount);
    }

    [Fact]
    public async Task ACredentialSetOnTheMirrorRepositoryIsUsedByTheDefaultRepository()
    {
        var wire = _app.Services.GetRequiredService<IRemoteHttpClientFactory>() as ForwardingRemoteHttpClientFactory;
        Assert.NotNull(wire);

        using (var scope = _app.Services.CreateScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();
            var settings = (await admin.FindAsync(Repository.MirrorName, CancellationToken.None))!.Settings;
            settings.Username = "bob";
            settings.Password = "pw";
            await admin.UpdateAsync(Repository.MirrorName, settings, clearPassword: false, CancellationToken.None);
        }

        using var client = _app.CreateClient();
        (await client.GetAsync("v3/package/testdata/index.json", TestContext.Current.CancellationToken)).Dispose();

        Assert.NotEmpty(wire!.Requests);
        Assert.All(wire.Requests, r => Assert.StartsWith("Basic ", r.Authorization));
    }

    [Fact]
    public async Task ChangingOnlyAMirrorTimeoutKeepsTheStoredUserNameAndPassword()
    {
        using (var scope = _app.Services.CreateScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();
            var settings = (await admin.FindAsync(Repository.MirrorName, CancellationToken.None))!.Settings;
            settings.Username = "bob";
            settings.Password = "pw";
            await admin.UpdateAsync(Repository.MirrorName, settings, clearPassword: false, CancellationToken.None);
        }

        // Same origin, only the timeout differs (what a restart with a changed Mirror:* does).
        using (var scope = _app.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IOptions<MirrorOptions>>().Value.PackageDownloadTimeoutSeconds = 123;
            await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().EnsureMirrorRepositoryAsync(CancellationToken.None);
        }

        using (var scope = _app.Services.CreateScope())
        {
            var detail = (await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().FindAsync(Repository.MirrorName, CancellationToken.None))!;
            Assert.Equal(123, detail.Settings.DownloadTimeoutSeconds);
            Assert.Equal("bob", detail.Settings.Username);
            Assert.True(detail.HasPassword);
        }
    }
}
