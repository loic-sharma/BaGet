using System;
using System.Collections.Generic;
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
/// The JSON twins (<c>/api/ui/admin/...</c>) of the core administration screens (users, account detail and deletion, user and ownerless packages, organizations,
/// settings, operations, validation, abuse, cluster, formats, access notice): who may call them (anonymous / user / administrator cookie (never accepted) / <c>admin:api</c> key /
/// other keys), the read-only gate, that the answers carry what the page shows and that nothing secret leaves the server.
/// </summary>
public class AdminCoreUiApiTests : IDisposable
{
    private const string GlobalKey = "global-key";

    private readonly BaGetApplication _app;
    private readonly Browser _anonymous;
    private readonly Browser _alice;
    private readonly Browser _root;
    private string _adminKey = string.Empty;
    private string _pushOnlyAdminKey = string.Empty;
    private string _aliceKey = string.Empty;
    private string _limitedAdminKey = string.Empty;

    public AdminCoreUiApiTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "true" }, { "ApiKey", GlobalKey } },
            configureServices: services => services.AddSingleton<IAdminOperation>(new CoreUiApiProbeOperation()));
        _anonymous = new Browser(_app);
        _alice = new Browser(_app);
        _root = new Browser(_app);
        SetUpAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _anonymous.Dispose();
        _alice.Dispose();
        _root.Dispose();
        _app.Dispose();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task SetUpAsync()
    {
        await CreateUserAsync(_app, "alice", email: "alice@example.com");
        await CreateUserAsync(_app, "bob");
        await CreateUserAsync(_app, "root", administrator: true);
        await SignInAsync(_alice, "alice");
        await SignInAsync(_root, "root");
        await AddOrganizationAsync(_app, "acme", ("bob", true));

        var defaultKey = (await _app.Services.GetRequiredService<IRepositoryService>().FindAsync("default", Token))!.Key;
        _adminKey = await KeyAsync("root", new[] { ApiKeyScopes.Admin });
        _pushOnlyAdminKey = await KeyAsync("root", new[] { ApiKeyScopes.Push });
        _limitedAdminKey = await KeyAsync("root", new[] { ApiKeyScopes.Admin }, new[] { defaultKey });
        _aliceKey = await KeyAsync("alice", new[] { ApiKeyScopes.Push });
    }

    private async Task<string> KeyAsync(string user, string[] scopes, int[]? repositories = null)
    {
        using var scope = _app.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ApiKeyService>()
            .CreateAsync((await FindAsync(_app, user))!.Id, "test key of " + user, scopes, "*", null, repositories, Token);
    }

    private sealed record Answer(HttpStatusCode Status, JsonNode? Json, HttpResponseHeaders Headers, string Body);

    private async Task<Answer> SendAsync(Browser browser, HttpMethod method, string path, object? body = null, string? key = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (key != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        if (method != HttpMethod.Get)
        {
            var json = body == null ? "{}" : System.Text.Json.JsonSerializer.Serialize(body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await browser.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync(Token);

        return new Answer(response.StatusCode, text.Length > 0 && text[0] == '{' ? JsonNode.Parse(text) : null, response.Headers, text);
    }

    private Task<Answer> AsAdminAsync(HttpMethod method, string path, object? body = null) => SendAsync(_anonymous, method, path, body, key: _adminKey);

    private static string Status(Answer answer) => answer.Json!["status"]!.GetValue<string>();

    // ---- who may open a screen ----

    public static TheoryData<string> ReadScreens => new()
    {
        "/api/ui/admin/users",
        "/api/ui/admin/users/alice",
        "/api/ui/admin/users/alice/delete",
        "/api/ui/admin/users/alice/packages",
        "/api/ui/admin/organizations",
        "/api/ui/admin/organizations/acme",
        "/api/ui/admin/packages",
        "/api/ui/admin/settings",
        "/api/ui/admin/settings/copy-status",
        "/api/ui/admin/operations",
        "/api/ui/admin/validation",
        "/api/ui/admin/abuse",
        "/api/ui/admin/cluster",
        "/api/ui/admin/formats",
        "/api/ui/admin/access-unavailable",
    };

    [Theory]
    [MemberData(nameof(ReadScreens))]
    public async Task AnAdministratorScreenIsAnAdminApiKeyAndNobodyElse(string path)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(_anonymous, HttpMethod.Get, path)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(_alice, HttpMethod.Get, path)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(_root, HttpMethod.Get, path)).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(_anonymous, HttpMethod.Get, path, key: _adminKey)).Status);
        // The global key has no account (the screens act as the signed-in user): refused on every administration screen.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_anonymous, HttpMethod.Get, path, key: GlobalKey)).Status);

        // A key without the administration scope, a limited one and a plain user's key.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_anonymous, HttpMethod.Get, path, key: _pushOnlyAdminKey)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_anonymous, HttpMethod.Get, path, key: _limitedAdminKey)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_anonymous, HttpMethod.Get, path, key: _aliceKey)).Status);
    }

    [Fact]
    public async Task AnUnknownAccountOrOrganizationIsAJson404()
    {
        foreach (var path in new[] { "/api/ui/admin/users/nobody", "/api/ui/admin/users/nobody/delete", "/api/ui/admin/users/nobody/packages", "/api/ui/admin/organizations/nobody" })
        {
            var answer = await AsAdminAsync(HttpMethod.Get, path);

            Assert.Equal(HttpStatusCode.NotFound, answer.Status);
            Assert.Equal("NotFound", Status(answer));
        }
    }

    // ---- writes ----

    [Fact]
    public async Task ACookieWriteIs401AndAKeyWriteNeedsNoAntiforgeryHeader()
    {
        var body = new { userName = "alice", password = Password };

        var refused = await SendAsync(_root, HttpMethod.Post, "/api/ui/admin/users/promote", body);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.Status);
        Assert.False((await FindAsync(_app, "alice"))!.IsAdministrator);

        var withKey = await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/admin/users/promote", body, key: _adminKey);
        Assert.Equal(HttpStatusCode.OK, withKey.Status);
        Assert.Equal("Next", Status(withKey));
        Assert.Equal("AdminUsers/Index", JsonScreen.Name(withKey.Json!["next"]!["screen"]!));
        Assert.Equal("alice is now an administrator.", withKey.Json["next"]!["message"]!.GetValue<string>());
        Assert.True((await FindAsync(_app, "alice"))!.IsAdministrator);

        // The global key is refused at the gate, not by a use case that finds no current user.
        var global = await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/admin/users/demote", body, key: GlobalKey);
        Assert.Equal(HttpStatusCode.Forbidden, global.Status);
        Assert.True((await FindAsync(_app, "alice"))!.IsAdministrator);
    }

    [Fact]
    public async Task AWrongPasswordChangesNothingAndTheAnswerSaysSo()
    {
        var answer = await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/admin/users/promote", new { userName = "alice", password = "wrong" }, key: _adminKey);

        Assert.Equal("Next", Status(answer));
        Assert.Contains("confirmation is incorrect", answer.Json!["next"]!["error"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.False((await FindAsync(_app, "alice"))!.IsAdministrator);
    }

    [Fact]
    public async Task EveryWriteIsRefusedWith503WhileTheServerIsReadOnly()
    {
        var gate = _app.Services.GetRequiredService<MaintenanceGate>();
        gate.Close("Copying the database");
        try
        {
            foreach (var (path, body) in new (string, object)[]
            {
                ("/api/ui/admin/users/promote", new { userName = "alice", password = Password }),
                ("/api/ui/admin/users/create", new { newUserName = "carol", password = Password }),
                ("/api/ui/admin/users/alice/approve", new { password = Password }),
                ("/api/ui/admin/organizations/acme/rename", new { newName = "acme2", password = Password }),
                ("/api/ui/admin/settings", new { section = "Site", password = Password }),
                ("/api/ui/admin/settings/set-log-level", new { source = "Foo", level = "Warning", password = Password }),
            })
            {
                var answer = await SendAsync(_anonymous, HttpMethod.Post, path, body, key: _adminKey);

                Assert.True(answer.Status == HttpStatusCode.ServiceUnavailable, $"{path} answered {answer.Status}");
            }

            // Reading still works.
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/admin/users", key: _adminKey)).Status);
        }
        finally
        {
            gate.Open();
        }
    }

    [Fact]
    public async Task AGeneratedPasswordIsInTheAnswerOnceAndNeverCached()
    {
        var reset = await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/admin/users/reset-password", new { userName = "alice", password = Password }, key: _adminKey);

        Assert.Equal("Ok", Status(reset));
        Assert.False(string.IsNullOrEmpty(reset.Json!["model"]!["generatedPassword"]!.GetValue<string>()));
        Assert.Equal("alice", reset.Json["model"]!["generatedFor"]!.GetValue<string>());
        Assert.True(reset.Headers.CacheControl?.NoStore);

        var created = await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/admin/users/create", new { newUserName = "carol", password = Password }, key: _adminKey);

        Assert.Equal("Ok", Status(created));
        Assert.True(created.Json!["model"]!["generatedForNewUser"]!.GetValue<bool>());
        Assert.True(created.Headers.CacheControl?.NoStore);
        Assert.NotNull(await FindAsync(_app, "carol"));

        // The next read of the list does not repeat it.
        var list = await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/admin/users", key: _adminKey);
        Assert.Null(list.Json!["model"]!["generatedPassword"]);
    }

    [Fact]
    public async Task ResettingYourOwnPasswordIsRefused()
    {
        var answer = await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/admin/users/reset-password", new { userName = "root", password = Password }, key: _adminKey);

        Assert.Equal("Next", Status(answer));
        Assert.Contains("your own password", answer.Json!["next"]!["error"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeletingAnAccountThroughTheTwinRemovesItAndPointsAtTheList()
    {
        var preview = await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/admin/users/bob/delete", key: _adminKey);
        Assert.Equal("Ok", Status(preview));
        Assert.Equal("keep", preview.Json!["model"]!["soleOwned"]!.GetValue<string>());

        var refused = await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/admin/users/bob/delete", new { soleOwned = "keep", password = "wrong" }, key: _adminKey);
        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.Equal("Invalid", Status(refused));
        Assert.NotNull(await FindAsync(_app, "bob"));

        var deleted = await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/admin/users/bob/delete", new { soleOwned = "keep", password = Password }, key: _adminKey);
        Assert.Equal("Next", Status(deleted));
        Assert.Equal("AdminUsers/Index", JsonScreen.Name(deleted.Json!["next"]!["screen"]!));
        Assert.Null(await FindAsync(_app, "bob"));
    }

    [Fact]
    public async Task ARenamedOrganizationLeadsToItsNewName()
    {
        var answer = await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/admin/organizations/acme/rename", new { newName = "acme2", password = Password }, key: _adminKey);

        Assert.Equal("Next", Status(answer));
        Assert.Equal("AdminOrganizations/Detail", JsonScreen.Name(answer.Json!["next"]!["screen"]!));
        Assert.Equal("acme2", answer.Json["next"]!["routeValues"]!["name"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, (await AsAdminAsync(HttpMethod.Get, "/api/ui/admin/organizations/acme2")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await AsAdminAsync(HttpMethod.Get, "/api/ui/admin/organizations/acme")).Status);
    }

    [Fact]
    public async Task ASettingsSaveThatIsRefusedAnswers400WithTheScreenAndTheReasons()
    {
        var answer = await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/admin/settings", new { section = "Site", password = "wrong", fields = new Dictionary<string, string[]>() }, key: _adminKey);

        Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
        Assert.Equal("Invalid", Status(answer));
        Assert.Contains("confirmation is incorrect", answer.Json!["errors"]![""]![0]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.NotNull(answer.Json["model"]!["sectionList"]);

        var saved = await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/admin/settings", new { section = "Site", password = Password, fields = new Dictionary<string, string[]>() }, key: _adminKey);
        Assert.Equal("Next", Status(saved));
        Assert.Equal("Nothing changed.", saved.Json!["next"]!["message"]!.GetValue<string>());
        Assert.StartsWith("site", saved.Json["next"]!["fragment"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ALogLevelSetThroughTheTwinIsListedByTheScreen()
    {
        var set = await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/admin/settings/set-log-level", new { source = "Some.Namespace", level = "Warning", password = Password }, key: _adminKey);
        Assert.Equal("Next", Status(set));

        var screen = await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/admin/settings", key: _adminKey);
        var levels = screen.Json!["model"]!["logLevels"]!.AsArray();
        Assert.Contains(levels, level => level!["source"]!.GetValue<string>() == "Some.Namespace" && level["level"]!.GetValue<string>() == "Warning");

        var removed = await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/admin/settings/remove-log-level", new { source = "Some.Namespace", password = Password }, key: _adminKey);
        Assert.Equal("Next", Status(removed));
    }

    // ---- what the page shows is what the JSON carries ----

    [Fact]
    public async Task TheUserListPageAndItsTwinListTheSameAccounts()
    {
        var page = await _root.GetPageAsync("/admin/users");
        var json = (await AsAdminAsync(HttpMethod.Get, "/api/ui/admin/users")).Json!["model"]!;

        var names = json["users"]!.AsArray().Select(u => u!["userName"]!.GetValue<string>()).ToList();
        Assert.Equal(new[] { "alice", "bob", "root" }, names);
        foreach (var name in names) Assert.Contains(name, page, StringComparison.Ordinal);
        Assert.Equal("root", json["currentUserName"]!.GetValue<string>());

        var filtered = (await AsAdminAsync(HttpMethod.Get, "/api/ui/admin/users?q=ali")).Json!["model"]!["users"]!.AsArray();
        Assert.Single(filtered);
    }

    [Fact]
    public async Task TheAccountPageAndItsTwinAgree()
    {
        var page = await _root.GetPageAsync("/admin/users/alice");
        var json = (await AsAdminAsync(HttpMethod.Get, "/api/ui/admin/users/alice")).Json!["model"]!;

        Assert.Equal("alice", json["account"]!["userName"]!.GetValue<string>());
        Assert.Equal("alice@example.com", json["account"]!["email"]!.GetValue<string>());
        Assert.Contains("alice@example.com", page, StringComparison.Ordinal);
        Assert.Equal("test key of alice", json["keys"]![0]!["description"]!.GetValue<string>());
        Assert.Equal("all repositories", json["keys"]![0]!["repositories"]!.GetValue<string>());
        Assert.Contains("test key of alice", page, StringComparison.Ordinal);
        Assert.True(json["modelAvailable"]!.GetValue<bool>());
        Assert.NotEmpty(json["access"]!.AsArray());
    }

    [Fact]
    public async Task TheOrganizationAndTheClusterAndTheFormatsAgreeWithTheirPages()
    {
        var organization = (await AsAdminAsync(HttpMethod.Get, "/api/ui/admin/organizations/acme")).Json!["model"]!;
        Assert.Equal("acme", organization["organization"]!["name"]!.GetValue<string>());
        Assert.Equal("bob", organization["members"]![0]!["userName"]!.GetValue<string>());
        Assert.True(organization["members"]![0]!["isAdmin"]!.GetValue<bool>());
        Assert.Contains("bob", await _root.GetPageAsync("/admin/organizations/acme"), StringComparison.Ordinal);

        var list = (await AsAdminAsync(HttpMethod.Get, "/api/ui/admin/organizations")).Json!["model"]!["organizations"]!.AsArray();
        Assert.Equal("acme", list.Single()!["name"]!.GetValue<string>());

        var cluster = (await AsAdminAsync(HttpMethod.Get, "/api/ui/admin/cluster")).Json!["model"]!["status"]!;
        Assert.False(cluster["enabled"]!.GetValue<bool>());
        Assert.Contains(cluster["thisNode"]!.GetValue<string>(), await _root.GetPageAsync("/admin/cluster"), StringComparison.Ordinal);

        var formats = (await AsAdminAsync(HttpMethod.Get, "/api/ui/admin/formats")).Json!["model"]!["rows"]!.AsArray();
        var displayNames = formats.Select(r => r!["module"]!["displayName"]!.GetValue<string>()).ToList();
        Assert.Contains("NuGet", displayNames);
        var page = await _root.GetPageAsync("/admin/formats");
        foreach (var name in displayNames) Assert.Contains(name, page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheOperationsAndTheValidationAndTheAbuseScreensAnswerLikeTheirPages()
    {
        var operations = (await AsAdminAsync(HttpMethod.Get, "/api/ui/admin/operations")).Json!["model"]!;
        Assert.NotEmpty(operations["rows"]!.AsArray());
        Assert.Contains(operations["rows"]![0]!["operation"]!["title"]!.GetValue<string>(), await _root.GetPageAsync("/admin/operations"), StringComparison.Ordinal);

        var started = await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/admin/operations", new { operation = "probe", repository = "default" }, key: _adminKey);
        Assert.Equal("Probe operation started on default.", started.Json!["next"]!["message"]!.GetValue<string>());

        var unknown = await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/admin/operations", new { operation = "nope", repository = "default", password = Password }, key: _adminKey);
        Assert.Equal(HttpStatusCode.NotFound, unknown.Status);

        var validation = (await AsAdminAsync(HttpMethod.Get, "/api/ui/admin/validation")).Json!["model"]!;
        Assert.False(validation["isSupported"]!.GetValue<bool>() && validation["submissions"]!.AsArray().Count > 0);

        var abuse = await AsAdminAsync(HttpMethod.Get, "/api/ui/admin/abuse?all=true");
        Assert.True(abuse.Json!["model"]!["all"]!.GetValue<bool>());
        var missing = await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/admin/abuse/resolve", new { key = 12345, all = false }, key: _adminKey);
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
    }

    [Fact]
    public async Task TheCopyStatusOfTheTwinAndOfThePageHandlerAreTheSameJson()
    {
        var page = await _root.GetAsync("/admin/settings?handler=CopyStatus");
        var pageJson = JsonNode.Parse(await page.Content.ReadAsStringAsync(Token))!;
        var twin = (await AsAdminAsync(HttpMethod.Get, "/api/ui/admin/settings/copy-status")).Json!["model"]!;

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("Idle", pageJson["phase"]!.GetValue<string>());
        Assert.Equal(pageJson["phase"]!.GetValue<string>(), twin["phase"]!.GetValue<string>());
        Assert.Equal(pageJson["active"]!.GetValue<bool>(), twin["active"]!.GetValue<bool>());
    }

    // ---- no secrets ----

    [Fact]
    public async Task NoAdministrationResponseCarriesASecret()
    {
        string aliceKeyHash;
        using (var scope = _app.Services.CreateScope())
        {
            var keys = await scope.ServiceProvider.GetRequiredService<ApiKeyService>().ListAsync((await FindAsync(_app, "alice"))!.Id, Token);
            aliceKeyHash = keys.First().HashedKey!;
        }

        foreach (var path in new[]
                 {
                     "/api/ui/admin/users", "/api/ui/admin/users/alice", "/api/ui/admin/users/alice/delete", "/api/ui/admin/organizations/acme",
                     "/api/ui/admin/settings", "/api/ui/admin/operations", "/api/ui/admin/cluster", "/api/ui/admin/formats",
                 })
        {
            var answer = await SendAsync(_anonymous, HttpMethod.Get, path, key: _adminKey);

            Assert.Equal(HttpStatusCode.OK, answer.Status);
            Assert.DoesNotContain(_adminKey, answer.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(_aliceKey, answer.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(aliceKeyHash, answer.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(GlobalKey, answer.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(Password, answer.Body, StringComparison.Ordinal);
            foreach (var word in new[] { "passwordHash", "securityStamp", "hashedKey", "concurrencyStamp", "protectedPassword", "connectionString\":\"" })
            {
                Assert.DoesNotContain(word, answer.Body, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}

/// <summary>An operation that does nothing, so the Operations screen has a row.</summary>
internal sealed class CoreUiApiProbeOperation : IAdminOperation
{
    public string Name => "probe";

    public string Title => "Probe operation";

    public string Description => "Does nothing.";

    public bool IsAvailable(IServiceProvider services) => true;

    public Task<string> RunAsync(IServiceProvider services, CancellationToken cancellationToken) => Task.FromResult("done");
}

/// <summary>While accounts are off the administration screens do not exist: a JSON 404, never a failure to build the controller.</summary>
public class AdminCoreUiApiAccountsOffTests : IDisposable
{
    private readonly BaGetApplication _app;

    public AdminCoreUiApiAccountsOffTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output);
    }

    public void Dispose() => _app.Dispose();

    [Theory]
    [InlineData("/api/ui/admin/users")]
    [InlineData("/api/ui/admin/users/alice")]
    [InlineData("/api/ui/admin/organizations")]
    [InlineData("/api/ui/admin/packages")]
    [InlineData("/api/ui/admin/settings")]
    [InlineData("/api/ui/admin/operations")]
    [InlineData("/api/ui/admin/cluster")]
    [InlineData("/api/ui/admin/access-unavailable")]
    public async Task TheScreensAreAJson404(string path)
    {
        using var client = _app.CreateClient();
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("NotFound", JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["status"]!.GetValue<string>());
    }
}
