using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
/// The JSON twin (<c>/api/ui/admin/repositories</c>) of the repository screens (list and create forms, one repository with all its handlers, deletion): who may
/// call it, the read-only gate, that the answers carry what the pages show, that each write does what the page's form does, and that no
/// answer ever carries an upstream password.
/// </summary>
public class AdminRepositoriesUiApiTests : IDisposable
{
    private const string GlobalKey = "global-key";
    private const string UpstreamPassword = "upstream-s3cret-token";

    private readonly BaGetApplication _app;
    private readonly Browser _anonymous;
    private readonly Browser _alice;
    private readonly Browser _root;
    private string _adminKey = string.Empty;
    private string _pushOnlyAdminKey = string.Empty;
    private string _limitedAdminKey = string.Empty;
    private string _aliceKey = string.Empty;

    public AdminRepositoriesUiApiTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "true" }, { "ApiKey", GlobalKey } });
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
        await CreateUserAsync(_app, "alice");
        await CreateUserAsync(_app, "root", administrator: true);
        await SignInAsync(_alice, "alice");
        await SignInAsync(_root, "root");

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

    private sealed record Answer(HttpStatusCode Status, JsonNode? Json, string Body);

    private async Task<Answer> SendAsync(Browser browser, HttpMethod method, string path, object? body = null, string? key = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (key != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        if (method != HttpMethod.Get)
        {
            var json = body == null ? "{}" : JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await browser.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync(Token);

        return new Answer(response.StatusCode, text.Length > 0 && text[0] == '{' ? JsonNode.Parse(text) : null, text);
    }

    private Task<Answer> AdminAsync(HttpMethod method, string path, object? body = null) => SendAsync(_anonymous, method, path, body, key: _adminKey);

    private static string Status(Answer answer) => answer.Json!["status"]!.GetValue<string>();

    private static object Remote(string url = "http://localhost/v3/index.json", string? password = null)
        => new { url, protocol = "V3", username = password == null ? null : "user", password, metadataTtlSeconds = 600, negativeTtlSeconds = 60, downloadTimeoutSeconds = 600, storeLocally = true };

    private async Task<RepositorySummary> SummaryAsync(string name)
    {
        using var scope = _app.Services.CreateScope();

        return (await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().FindAsync(name, Token))!;
    }

    private async Task CreateAsync(string name) => Assert.Equal("Next", Status(await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories", new { name })));

    // ---- who may open a screen ----

    [Theory]
    [InlineData("/api/ui/admin/repositories")]
    [InlineData("/api/ui/admin/repositories?kind=Local")]
    [InlineData("/api/ui/admin/repositories/default")]
    [InlineData("/api/ui/admin/repositories/default/delete")]
    public async Task ARepositoryScreenIsAnAdminApiKeyAndNobodyElse(string path)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(_anonymous, HttpMethod.Get, path)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(_alice, HttpMethod.Get, path)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(_root, HttpMethod.Get, path)).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(_anonymous, HttpMethod.Get, path, key: _adminKey)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_anonymous, HttpMethod.Get, path, key: GlobalKey)).Status);

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_anonymous, HttpMethod.Get, path, key: _pushOnlyAdminKey)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_anonymous, HttpMethod.Get, path, key: _limitedAdminKey)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_anonymous, HttpMethod.Get, path, key: _aliceKey)).Status);
    }

    [Fact]
    public async Task AnUnknownRepositoryIsAJson404()
    {
        foreach (var path in new[] { "/api/ui/admin/repositories/nobody", "/api/ui/admin/repositories/nobody/delete" })
        {
            var answer = await AdminAsync(HttpMethod.Get, path);

            Assert.Equal(HttpStatusCode.NotFound, answer.Status);
            Assert.Equal("NotFound", Status(answer));
        }
    }

    [Fact]
    public async Task ARepositoryNamedLikeACreateHandlerIsStillReachable()
    {
        await CreateAsync("format");
        await CreateAsync("create");

        Assert.Equal(HttpStatusCode.OK, (await AdminAsync(HttpMethod.Get, "/api/ui/admin/repositories/format")).Status);
        Assert.Equal(HttpStatusCode.OK, (await AdminAsync(HttpMethod.Get, "/api/ui/admin/repositories/create")).Status);

        var saved = await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/format", new { anonymousRead = false });
        Assert.Equal("Next", Status(saved));
        Assert.False((await SummaryAsync("format")).AnonymousRead);
    }

    // ---- writes ----

    [Fact]
    public async Task ACookieWriteIs401AndAKeyWriteNeedsNoAntiforgeryHeader()
    {
        var refused = await SendAsync(_root, HttpMethod.Post, "/api/ui/admin/repositories", new { name = "cookie" });
        Assert.Equal(HttpStatusCode.Unauthorized, refused.Status);
        Assert.Null(await _app.Services.GetRequiredService<IRepositoryService>().FindAsync("cookie", Token));

        var withKey = await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories", new { name = "keyed" });
        Assert.Equal("Next", Status(withKey));
        Assert.Equal("AdminRepositories/Index", JsonScreen.Name(withKey.Json!["next"]!["screen"]!));
        Assert.Equal("Repository keyed was created.", withKey.Json["next"]!["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task EveryWriteIsRefusedWith503WhileTheServerIsReadOnly()
    {
        await CreateAsync("up");
        var gate = _app.Services.GetRequiredService<MaintenanceGate>();
        gate.Close("Copying the database");
        try
        {
            foreach (var (path, body) in new (string, object)[]
            {
                ("/api/ui/admin/repositories", new { name = "x" }),
                ("/api/ui/admin/repositories/create/format", new { name = "x", format = "Npm" }),
                ("/api/ui/admin/repositories/create/virtual", new { name = "x" }),
                ("/api/ui/admin/repositories/create/remote", new { name = "x", remote = Remote(), password = Password }),
                ("/api/ui/admin/repositories/up", new { anonymousRead = false }),
                ("/api/ui/admin/repositories/up/purge", new { password = Password }),
                ("/api/ui/admin/repositories/up/add-member", new { member = "default" }),
                ("/api/ui/admin/repositories/up/delete", new { }),
            })
            {
                var answer = await AdminAsync(HttpMethod.Post, path, body);

                Assert.True(answer.Status == HttpStatusCode.ServiceUnavailable, $"{path} answered {answer.Status}");
            }

            Assert.Equal(HttpStatusCode.OK, (await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/admin/repositories", key: _adminKey)).Status);
        }
        finally
        {
            gate.Open();
        }
    }

    [Fact]
    public async Task ABadNameIsRefusedWith400AndTheScreenKeepsWhatWasTyped()
    {
        var answer = await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories", new { name = "Bad Name" });

        Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
        Assert.Equal("Invalid", Status(answer));
        Assert.Equal("Bad Name", answer.Json!["model"]!["name"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(answer.Json["model"]!["error"]!.GetValue<string>()));
        Assert.NotEmpty(answer.Json["model"]!["repositories"]!.AsArray());

        var duplicate = await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories", new { name = "default" });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.Status);
    }

    [Fact]
    public async Task EveryCreateFormCreatesWhatThePageCreates()
    {
        var npm = await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/create/format", new { name = "mynpm", format = "Npm", anonymousRead = false });
        Assert.Equal("Next", Status(npm));
        var created = (await SummaryAsync("mynpm"));
        Assert.Equal(RepositoryFormat.Npm, created.Format);
        Assert.False(created.AnonymousRead);

        Assert.Equal(HttpStatusCode.NotFound, (await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/create/format", new { name = "z", format = "NuGet" })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/create/format", new { name = "z", format = "Nope" })).Status);

        var virtualRepository = await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/create/virtual", new { name = "everything" });
        Assert.Equal("Next", Status(virtualRepository));
        Assert.Equal("AdminRepositories/Edit", JsonScreen.Name(virtualRepository.Json!["next"]!["screen"]!));
        Assert.Equal("everything", virtualRepository.Json["next"]!["routeValues"]!["name"]!.GetValue<string>());
        Assert.Equal(RepositoryKind.Virtual, (await _app.Services.GetRequiredService<IRepositoryService>().FindAsync("everything", Token))!.Kind);

        var remote = await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/create/remote", new { name = "upstream", remote = Remote(), password = Password });
        Assert.True(Status(remote) == "Next", remote.Body);
        Assert.Equal(RepositoryKind.Remote, (await _app.Services.GetRequiredService<IRepositoryService>().FindAsync("upstream", Token))!.Kind);
    }

    [Fact]
    public async Task ARemoteRepositoryNeedsThePasswordAndNoAnswerEverCarriesTheUpstreamPassword()
    {
        var wrong = await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/create/remote", new { name = "secured", remote = Remote(password: UpstreamPassword), password = "wrong" });

        Assert.Equal(HttpStatusCode.BadRequest, wrong.Status);
        Assert.Contains("confirmation is incorrect", wrong.Json!["model"]!["error"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.DoesNotContain(UpstreamPassword, wrong.Body, StringComparison.Ordinal);
        Assert.Null(await _app.Services.GetRequiredService<IRepositoryService>().FindAsync("secured", Token));

        var created = await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/create/remote", new { name = "secured", remote = Remote(password: UpstreamPassword), password = Password });
        Assert.Equal("Next", Status(created));
        Assert.DoesNotContain(UpstreamPassword, created.Body, StringComparison.Ordinal);

        // A remote repository created with credentials is not anonymously readable unless chosen.
        Assert.False((await SummaryAsync("secured")).AnonymousRead);

        foreach (var path in new[] { "/api/ui/admin/repositories/secured", "/api/ui/admin/repositories" })
        {
            var screen = await AdminAsync(HttpMethod.Get, path);

            Assert.Equal(HttpStatusCode.OK, screen.Status);
            Assert.DoesNotContain(UpstreamPassword, screen.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("protectedPassword", screen.Body, StringComparison.OrdinalIgnoreCase);
        }

        var detail = (await AdminAsync(HttpMethod.Get, "/api/ui/admin/repositories/secured")).Json!["model"]!;
        Assert.True(detail["remote"]!["hasPassword"]!.GetValue<bool>());
        Assert.Equal("user", detail["remote"]!["username"]!.GetValue<string>());

        // A refused save (wrong password) shows the screen again without the typed upstream password either.
        var refused = await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/secured", new { remote = Remote(password: "another-" + UpstreamPassword), password = "wrong" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.DoesNotContain(UpstreamPassword, refused.Body, StringComparison.Ordinal);

        var test = await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/secured/test", new { remote = Remote(password: UpstreamPassword), password = Password });
        Assert.Equal(HttpStatusCode.OK, test.Status);
        Assert.NotNull(test.Json!["model"]!["testResult"]);
        Assert.DoesNotContain(UpstreamPassword, test.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SavingAndPurgingARemoteRepositoryAskForThePassword()
    {
        await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/create/remote", new { name = "cache", remote = Remote(), password = Password });

        var wrong = await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/cache/purge", new { password = "wrong" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.Status);

        var purged = await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/cache/purge", new { password = Password });
        Assert.Equal("Next", Status(purged));
        Assert.StartsWith("The cache of cache was purged", purged.Json!["next"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);

        var emptyId = await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/cache/purge-package", new { password = Password, packageId = " " });
        Assert.Equal(HttpStatusCode.BadRequest, emptyId.Status);
        Assert.Equal("Enter the id of the package to purge.", emptyId.Json!["model"]!["error"]!.GetValue<string>());

        var badVersion = await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/cache/purge-package", new { password = Password, packageId = "x", packageVersion = "not a version" });
        Assert.Equal(HttpStatusCode.BadRequest, badVersion.Status);

        var saved = await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/cache", new { anonymousRead = true, remote = Remote("http://localhost/other/index.json"), password = Password });
        Assert.Equal("Next", Status(saved));
        Assert.Equal("http://localhost/other/index.json", (await AdminAsync(HttpMethod.Get, "/api/ui/admin/repositories/cache")).Json!["model"]!["remote"]!["url"]!.GetValue<string>());

        // A purge or test of a repository that is not remote is a 404.
        Assert.Equal(HttpStatusCode.NotFound, (await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/default/purge", new { password = Password })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/default/test", new { password = Password })).Status);
    }

    [Fact]
    public async Task AVirtualRepositoryIsEditedMemberByMember()
    {
        await CreateAsync("one");
        await CreateAsync("two");
        await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/create/virtual", new { name = "both" });

        foreach (var member in new[] { "one", "two" })
        {
            var added = await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/both/add-member", new { member });
            Assert.Equal("Next", Status(added));
            Assert.Equal("repository-section-virtual", added.Json!["next"]!["fragment"]!.GetValue<string>());
        }

        var duplicate = await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/both/add-member", new { member = "one" });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.Status);
        Assert.Equal("'one' is already a member.", duplicate.Json!["model"]!["error"]!.GetValue<string>());

        await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/both/move-member", new { member = "two", direction = "up" });
        await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/both/deploy", new { deploy = "one" });

        var screen = (await AdminAsync(HttpMethod.Get, "/api/ui/admin/repositories/both")).Json!["model"]!;
        Assert.Equal(new[] { "two", "one" }, screen["virtualDetail"]!["members"]!.AsArray().Select(m => m!["name"]!.GetValue<string>()).ToArray());
        Assert.Equal("one", screen["virtualDetail"]!["deployRepository"]!.GetValue<string>());

        var preview = await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/both/preview", new { previewPackageId = "Some.Package", previewVersion = "1.0.0" });
        Assert.Equal("Ok", Status(preview));
        Assert.Equal(2, preview.Json!["model"]!["preview"]!["steps"]!.AsArray().Count);

        var noId = await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/both/preview", new { previewVersion = "1.0.0" });
        Assert.Equal(HttpStatusCode.BadRequest, noId.Status);

        var removed = await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/both/remove-member", new { member = "one" });
        Assert.Equal("Next", Status(removed));
        var after = (await AdminAsync(HttpMethod.Get, "/api/ui/admin/repositories/both")).Json!["model"]!["virtualDetail"]!;
        Assert.Equal(new[] { "two" }, after["members"]!.AsArray().Select(m => m!["name"]!.GetValue<string>()).ToArray());
        Assert.Null(after["deployRepository"]);

        // Member handlers of a repository that is not virtual.
        Assert.Equal(HttpStatusCode.NotFound, (await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/default/add-member", new { member = "one" })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/default/preview", new { previewPackageId = "x", previewVersion = "1.0.0" })).Status);
    }

    [Fact]
    public async Task DeletingARepositoryAsksNothingButRefusesWhatIsProtectedOrReferenced()
    {
        await CreateAsync("gone");
        await CreateAsync("member");
        await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/create/virtual", new { name = "group" });
        await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/group/add-member", new { member = "member" });

        var preview = await AdminAsync(HttpMethod.Get, "/api/ui/admin/repositories/member/delete");
        Assert.Equal("group", preview.Json!["model"]!["referencedBy"]![0]!.GetValue<string>());

        var referenced = await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/member/delete");
        Assert.Equal(HttpStatusCode.BadRequest, referenced.Status);
        Assert.False(string.IsNullOrEmpty(referenced.Json!["model"]!["error"]!.GetValue<string>()));

        var protectedDefault = await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/default/delete");
        Assert.Equal(HttpStatusCode.BadRequest, protectedDefault.Status);

        var deleted = await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/gone/delete");
        Assert.Equal("Next", Status(deleted));
        Assert.Equal("Repository gone was deleted.", deleted.Json!["next"]!["message"]!.GetValue<string>());
        Assert.Null(await _app.Services.GetRequiredService<IRepositoryService>().FindAsync("gone", Token));
        Assert.Equal(HttpStatusCode.NotFound, (await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/gone/delete")).Status);
    }

    // ---- what the page shows is what the JSON carries ----

    [Fact]
    public async Task TheListPageAndItsTwinShowTheSameRepositoriesAndFeeds()
    {
        await CreateAsync("second");
        await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/create/format", new { name = "mynpm", format = "Npm" });

        var page = await _root.GetPageAsync("/admin/repositories");
        var model = (await AdminAsync(HttpMethod.Get, "/api/ui/admin/repositories")).Json!["model"]!;

        var names = model["repositories"]!.AsArray().Select(r => r!["name"]!.GetValue<string>()).ToList();
        Assert.Equal(new[] { "default", "mynpm", "second" }, names.OrderBy(n => n, StringComparer.Ordinal).ToArray());
        foreach (var name in names)
        {
            Assert.Contains(model["feeds"]![name]!.GetValue<string>(), page, StringComparison.Ordinal);
        }

        Assert.EndsWith("/r/second/v3/index.json", model["feeds"]!["second"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.NotEmpty(model["otherFormats"]!.AsArray());
        Assert.Contains(model["links"]!["mynpm"]!.AsArray(), link => link!["id"]!.GetValue<string>() == "browse");

        var filtered = (await AdminAsync(HttpMethod.Get, "/api/ui/admin/repositories?format=Npm")).Json!["model"]!["repositories"]!.AsArray();
        Assert.Equal("mynpm", filtered.Single()!["name"]!.GetValue<string>());
        Assert.Equal("Npm", (await AdminAsync(HttpMethod.Get, "/api/ui/admin/repositories?format=Npm")).Json!["model"]!["filterFormat"]!.GetValue<string>());
    }

    [Fact]
    public async Task TheRepositoryPageAndItsTwinAgree()
    {
        await CreateAsync("second");
        var page = await _root.GetPageAsync("/admin/repositories/second");
        var model = (await AdminAsync(HttpMethod.Get, "/api/ui/admin/repositories/second")).Json!["model"]!;

        Assert.Equal("second", model["name"]!.GetValue<string>());
        Assert.Contains(model["feedUrl"]!.GetValue<string>(), page, StringComparison.Ordinal);
        Assert.True(model["anonymousRead"]!.GetValue<bool>());
        Assert.Equal("Local", model["repository"]!["kind"]!.GetValue<string>());
        Assert.NotNull(model["access"]);
    }

    // ---- no secrets ----

    [Fact]
    public async Task NoRepositoryResponseCarriesASecret()
    {
        await AdminAsync(HttpMethod.Post, "/api/ui/admin/repositories/create/remote", new { name = "secured", remote = Remote(password: UpstreamPassword), password = Password });

        foreach (var path in new[] { "/api/ui/admin/repositories", "/api/ui/admin/repositories/secured", "/api/ui/admin/repositories/secured/delete", "/api/ui/admin/repositories/default" })
        {
            var answer = await AdminAsync(HttpMethod.Get, path);

            Assert.Equal(HttpStatusCode.OK, answer.Status);
            Assert.DoesNotContain(_adminKey, answer.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(_aliceKey, answer.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(GlobalKey, answer.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(UpstreamPassword, answer.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(Password, answer.Body, StringComparison.Ordinal);
            foreach (var word in new[] { "protectedPassword", "passwordHash", "hashedKey", "connectionString\":\"" })
            {
                Assert.DoesNotContain(word, answer.Body, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}

/// <summary>While accounts are off the repository screens do not exist: a JSON 404.</summary>
public class AdminRepositoriesUiApiAccountsOffTests : IDisposable
{
    private readonly BaGetApplication _app;

    public AdminRepositoriesUiApiAccountsOffTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output);
    }

    public void Dispose() => _app.Dispose();

    [Theory]
    [InlineData("/api/ui/admin/repositories")]
    [InlineData("/api/ui/admin/repositories/default")]
    [InlineData("/api/ui/admin/repositories/default/delete")]
    public async Task TheScreensAreAJson404(string path)
    {
        using var client = _app.CreateClient();
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("NotFound", JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["status"]!.GetValue<string>());
    }
}
