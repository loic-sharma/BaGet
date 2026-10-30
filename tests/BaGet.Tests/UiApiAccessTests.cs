using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Security;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// Authentication and the gates of <c>/api/ui</c> over HTTP, driven through the real pipeline with test twins that derive from each base of the family
/// (account, administrator, access). API keys authenticate (<c>Authorization: Bearer</c> or <c>X-NuGet-ApiKey</c>), like <c>/api/v1</c>; the bare browser
/// cookie and HTTP Basic never do (interactive sessions, and the cookie with an antiforgery header, are <c>UiApiSessionTests</c>). A track that adds a family of screens extends this class with its own cases.
/// </summary>
public class UiApiAccessTests : IDisposable
{
    private const string GlobalKey = "global-key";

    private readonly BaGetApplication _app;
    private readonly Browser _anonymous;
    private readonly Browser _alice;
    private readonly Browser _root;
    private string _rootKey = string.Empty;
    private string _aliceKey = string.Empty;
    private string _rootPushOnlyKey = string.Empty;

    public UiApiAccessTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "true" }, { "ApiKey", GlobalKey } },
            configureServices: services => services.AddControllers().AddApplicationPart(typeof(UiApiAccessTests).Assembly));
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
        await _app.CreateRepositoryAsync("second", Token);

        _rootKey = await KeyAsync("root", new[] { ApiKeyScopes.Admin });
        _rootPushOnlyKey = await KeyAsync("root", new[] { ApiKeyScopes.Push });
        _aliceKey = await KeyAsync("alice", new[] { ApiKeyScopes.Push });
    }

    private async Task<string> KeyAsync(string user, string[] scopes, int[]? repositories = null)
    {
        using var scope = _app.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ApiKeyService>()
            .CreateAsync((await FindAsync(_app, user))!.Id, "test", scopes, "*", null, repositories, Token);
    }

    private sealed record Answer(HttpStatusCode Status, JsonNode? Json, HttpResponseHeaders Headers, string Body);

    public enum Credential { Bearer, ApiKeyHeader, Basic }

    private async Task<Answer> SendAsync(Browser browser, HttpMethod method, string path, string? key = null, Credential how = Credential.Bearer)
    {
        using var request = new HttpRequestMessage(method, path);
        if (key != null)
        {
            switch (how)
            {
                case Credential.Bearer:
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                    break;
                case Credential.ApiKeyHeader:
                    request.Headers.Add("X-NuGet-ApiKey", key);
                    break;
                default:
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("root:" + key)));
                    break;
            }
        }

        if (method != HttpMethod.Get) request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await browser.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync(Token);

        return new Answer(response.StatusCode, body.Length > 0 && body[0] == '{' ? JsonNode.Parse(body) : null, response.Headers, body);
    }

    private static string FormWideError(Answer answer) => answer.Json!["errors"]![""]![0]!.GetValue<string>();

    // ---- no antiforgery, no cookie, no Basic ----

    [Fact]
    public async Task TheAntiforgeryTokenAloneAuthenticatesNothing()
    {
        // The token of GET /api/ui/antiforgery only accompanies a cookie session; without that cookie the header changes nothing.
        var token = (await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/antiforgery")).Json!["model"]!["requestToken"]!.GetValue<string>();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/ui/test-probe/admin");
        request.Headers.Add("X-XSRF-TOKEN", token);
        using var answer = await _anonymous.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
    }

    [Theory]
    [InlineData(Credential.Bearer)]
    [InlineData(Credential.ApiKeyHeader)]
    public async Task AKeyWriteNeedsNoAntiforgeryHeader(Credential how)
    {
        var answer = await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/test-probe/admin", key: _rootKey, how: how);

        Assert.Equal(HttpStatusCode.OK, answer.Status);
        Assert.Equal("admin", answer.Json!["model"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task HttpBasicIs401EvenWithAValidKeyAndTheMessageSaysWhatToUse(string method)
    {
        var answer = await SendAsync(_anonymous, new HttpMethod(method), "/api/ui/test-probe/admin", key: _rootKey, how: Credential.Basic);

        Assert.Equal(HttpStatusCode.Unauthorized, answer.Status);
        Assert.Equal("SignIn", answer.Json!["status"]!.GetValue<string>());
        Assert.Contains("Bearer", FormWideError(answer), StringComparison.Ordinal);
        Assert.Contains("X-NuGet-ApiKey", FormWideError(answer), StringComparison.Ordinal);
        Assert.Contains("Bearer", string.Join(",", answer.Headers.WwwAuthenticate), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BasicIsRefusedOnAPublicScreenToo()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/stats", key: _rootKey, how: Credential.Basic)).Status);
    }

    [Fact]
    public async Task ACookieIsNeverACredentialOnAnAdministratorTwin()
    {
        // The signed-in administrator's browser: no key means anonymous, so 401 (not 200 as the session would have given, not 404 as a user's would).
        var read = await SendAsync(_root, HttpMethod.Get, "/api/ui/test-probe/admin");
        var write = await SendAsync(_root, HttpMethod.Post, "/api/ui/test-probe/admin");
        var access = await SendAsync(_root, HttpMethod.Get, "/api/ui/test-probe/access");
        var screen = await SendAsync(_root, HttpMethod.Get, "/api/ui/admin/repositories");

        Assert.All(new[] { read, write, access, screen }, answer => Assert.Equal(HttpStatusCode.Unauthorized, answer.Status));
        Assert.Contains("Bearer", FormWideError(read), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AKeyWinsOverTheCookieOfAnotherUser()
    {
        // root's browser sends alice's key: the caller is alice (403), not the administrator behind the cookie.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_root, HttpMethod.Get, "/api/ui/test-probe/admin", key: _aliceKey)).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(_alice, HttpMethod.Get, "/api/ui/test-probe/admin", key: _rootKey)).Status);
    }

    [Fact]
    public async Task ACookieOnAPublicScreenSeesWhatAnAnonymousVisitorSees()
    {
        using (var package = TestResources.GetResourceStream(TestResources.Package))
        {
            await _app.AddPackageAsync(package, Token);
        }

        // The package screen offers the Manage link to somebody who may manage: the administrator's cookie is ignored, the administrator's key is not.
        var cookie = await SendAsync(_root, HttpMethod.Get, "/api/ui/packages/TestData/1.2.3");
        var key = await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/packages/TestData/1.2.3", key: _rootKey);

        Assert.Equal(HttpStatusCode.OK, cookie.Status);
        Assert.Null(cookie.Json!["model"]!["manageUrl"]);
        Assert.NotNull(key.Json!["model"]!["manageUrl"]);
    }

    [Fact]
    public async Task APrivateRepositoryIsNotOpenedByTheCookieOfAnAdministratorButByTheirKey()
    {
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
                .CreateAsync("closed", RepositoryFormat.NuGet, RepositoryKind.Local, new Dictionary<string, string?>(), anonymousRead: false, Token);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(_root, HttpMethod.Get, "/api/ui/r/closed/stats")).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/r/closed/stats", key: _rootKey)).Status);
    }

    [Fact]
    public async Task AnAnonymousWriteOnAPublicScreenReachesTheUseCaseWithoutAnyToken()
    {
        // Past authentication and (absent) antiforgery the empty form is refused with the reasons by field, not with a 400 about a token.
        var answer = await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/packages/TestData/1.2.3/report-abuse");

        Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
        Assert.Equal("Invalid", answer.Json!["status"]!.GetValue<string>());
        Assert.NotNull(answer.Json["errors"]!["Reason"]);
    }

    [Fact]
    public async Task CredentialsThatDoNotAuthenticateAre401NotAnonymous()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/stats", key: "not-a-key")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/stats", key: "not-a-key", how: Credential.ApiKeyHeader)).Status);
        // A valid cookie does not rescue a bad key.
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(_root, HttpMethod.Get, "/api/ui/test-probe/admin", key: "not-a-key")).Status);
    }

    [Fact]
    public async Task TheSessionCookieIsNotReadForTheReauthenticationProofEither()
    {
        // The re-authentication window lives in the session cookie; a request that is not allowed to use the cookie must not consult it.
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Items[AccountConfirmation.IgnoreSessionItem] = true;

        Assert.False(await AccountConfirmation.HasReauthenticatedRecentlyAsync(context));
    }

    // ---- the account base: screen only ----

    [Fact]
    public async Task AccountSelfServiceIsScreenOnlyAndEveryCallerOverHttpGets403WithAMessage()
    {
        var answers = new[]
        {
            await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/test-probe/account", key: _aliceKey),
            await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/test-probe/account", key: _aliceKey, how: Credential.ApiKeyHeader),
            await SendAsync(_alice, HttpMethod.Post, "/api/ui/test-probe/account"),
            await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/test-probe/account"),
            await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/test-probe/account", key: _rootKey),
        };

        foreach (var answer in answers)
        {
            Assert.Equal(HttpStatusCode.Forbidden, answer.Status);
            Assert.Equal("Forbidden", answer.Json!["status"]!.GetValue<string>());
            Assert.Contains("web screen", FormWideError(answer), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("/api/ui/account/login")]
    [InlineData("/api/ui/account/settings")]
    [InlineData("/api/ui/account/apikeys")]
    [InlineData("/api/ui/account/organizations")]
    public async Task TheRealAccountTwinsAreScreenOnly(string path)
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_alice, HttpMethod.Get, path)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_anonymous, HttpMethod.Get, path, key: _aliceKey)).Status);
    }

    // ---- the administrator base ----

    [Fact]
    public async Task AnAdministratorScreenIsAnAdminApiKeyAndNothingElse()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/test-probe/admin")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(_alice, HttpMethod.Get, "/api/ui/test-probe/admin")).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/test-probe/admin", key: _rootKey)).Status);
    }

    [Fact]
    public async Task AKeyWithoutTheAdminScopeOrOfANonAdministratorIs403OnAdministratorScreens()
    {
        // An administrator's key without admin:api, and a plain user's key.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/test-probe/admin", key: _rootPushOnlyKey)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/test-probe/admin", key: _aliceKey)).Status);
    }

    [Fact]
    public async Task TheGlobalKeyIsRefusedOnAdministratorTwinsWithAMessage()
    {
        // It has no owner, so the use cases that need the current user could not act for it: 403 at the gate, whatever the method.
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post })
        {
            foreach (var path in new[] { "/api/ui/test-probe/admin", "/api/ui/test-probe/access", method == HttpMethod.Get ? "/api/ui/admin/users" : "/api/ui/admin/users/promote", "/api/ui/admin/repositories" })
            {
                var answer = await SendAsync(_anonymous, method, path, key: GlobalKey);

                Assert.Equal(HttpStatusCode.Forbidden, answer.Status);
                Assert.Contains("global ApiKey", FormWideError(answer), StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task AKeyLimitedToARepositoryCannotAdminister()
    {
        var defaultKey = (await _app.Services.GetRequiredService<IRepositoryService>().FindAsync("default", Token))!.Key;
        var limited = await KeyAsync("root", new[] { ApiKeyScopes.Admin }, new[] { defaultKey });

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/test-probe/admin", key: limited)).Status);
    }

    // ---- the access base ----

    [Fact]
    public async Task AnAccessScreenIsRefusedWith503WhileTheServerIsReadOnly()
    {
        var gate = _app.Services.GetRequiredService<MaintenanceGate>();
        var reads = await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/test-probe/access", key: _rootKey);
        Assert.Equal(HttpStatusCode.OK, reads.Status);

        gate.Close("Copying the database");
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/test-probe/access", key: _rootKey)).Status);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/test-probe/access", key: _rootKey)).Status);
        }
        finally
        {
            gate.Open();
        }

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/test-probe/access", key: _rootKey)).Status);
    }

    [Fact]
    public async Task AnAccessScreenAdmitsWhomAuthorizeAsyncAdmitsAndNobodyElse()
    {
        // The probe admits nobody but server administrators.
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(_alice, HttpMethod.Get, "/api/ui/test-probe/access")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/test-probe/access", key: _aliceKey)).Status);
    }

    // ---- repository screens ----

    [Fact]
    public async Task ARepositoryScreenIsAnonymousWhereTheRepositoryIsOpenAndAnUnknownRepositoryIsAJson404()
    {
        var plain = await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/stats");
        var second = await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/r/second/stats");
        var unknown = await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/r/nope/stats");

        Assert.Equal(HttpStatusCode.OK, plain.Status);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Equal(HttpStatusCode.NotFound, unknown.Status);
        Assert.Equal("NotFound", unknown.Json!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task AKeyLimitedToRepositoriesSeesAnotherRepositoryAs404()
    {
        var defaultKey = (await _app.Services.GetRequiredService<IRepositoryService>().FindAsync("default", Token))!.Key;
        var limited = await KeyAsync("alice", new[] { ApiKeyScopes.Push }, new[] { defaultKey });

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/stats", key: limited)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/r/second/stats", key: limited)).Status);
    }

    // ---- browse screens: front page, package, report abuse, REST API page ----

    [Fact]
    public async Task TheFrontPageAndTheListAreAnonymousAndABadQueryIsAPlain400()
    {
        var home = await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/home");
        var list = await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/packages?q=anything");
        var secondHome = await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/r/second/home");

        Assert.Equal(HttpStatusCode.OK, home.Status);
        Assert.True(home.Json!["model"]!["isHome"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.OK, list.Status);
        Assert.False(list.Json!["model"]!["isHome"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.OK, secondHome.Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/packages?p=0")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/packages?sortBy=99")).Status);
    }

    [Fact]
    public async Task APackageScreenIsAnonymousAndOffersTheManageLinkOnlyToSomeoneWhoMayManage()
    {
        using (var package = TestResources.GetResourceStream(TestResources.Package))
        {
            await _app.AddPackageAsync(package, Token);
        }

        var anonymous = await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/packages/TestData/1.2.3");
        var missing = await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/packages/NoSuchPackage");
        var administrator = await SendAsync(_anonymous, HttpMethod.Get, "/api/ui/packages/TestData/1.2.3", key: _rootKey);

        Assert.Equal(HttpStatusCode.OK, anonymous.Status);
        Assert.True(anonymous.Json!["model"]!["found"]!.GetValue<bool>());
        Assert.Equal("TestData", anonymous.Json["model"]!["package"]!["id"]!.GetValue<string>());
        Assert.Null(anonymous.Json["model"]!["manageUrl"]);
        Assert.False(missing.Json!["model"]!["found"]!.GetValue<bool>());
        Assert.EndsWith("/packages/TestData/manage", administrator.Json!["model"]!["manageUrl"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReportAbusePostRefusesAnEmptyFormWithTheReasonsWhoeverSendsIt()
    {
        var anonymous = await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/packages/TestData/1.2.3/report-abuse");
        var withKey = await SendAsync(_anonymous, HttpMethod.Post, "/api/ui/packages/TestData/1.2.3/report-abuse", key: _aliceKey);
        foreach (var answer in new[] { anonymous, withKey })
        {
            Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
            Assert.Equal("Invalid", answer.Json!["status"]!.GetValue<string>());
            Assert.NotNull(answer.Json["errors"]!["Details"]);
            Assert.NotNull(answer.Json["errors"]!["Reason"]);
        }
    }

    [Fact]
    public async Task TheRestApiScreenIsForAdministratorKeysOnly()
    {
        const string path = "/api/ui/admin/api";

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(_anonymous, HttpMethod.Get, path)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(_root, HttpMethod.Get, path)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_anonymous, HttpMethod.Get, path, key: _rootPushOnlyKey)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_anonymous, HttpMethod.Get, path, key: _aliceKey)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_anonymous, HttpMethod.Get, path, key: GlobalKey)).Status);

        foreach (var answer in new[] { await SendAsync(_anonymous, HttpMethod.Get, path, key: _rootKey), await SendAsync(_anonymous, HttpMethod.Get, path, key: _rootKey, how: Credential.ApiKeyHeader) })
        {
            Assert.Equal(HttpStatusCode.OK, answer.Status);
            var model = answer.Json!["model"]!;
            Assert.EndsWith("/api/v1", model["apiBase"]!.GetValue<string>(), StringComparison.Ordinal);
            Assert.NotEmpty(model["endpoints"]!.AsArray());

            // Flat JSON: no reflection objects, no key.
            Assert.DoesNotContain("descriptor", answer.Body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(_rootKey, answer.Body, StringComparison.Ordinal);
        }
    }

    // ---- no secrets ----

    [Fact]
    public async Task NoResponseEchoesTheCredentialsOfTheRequest()
    {
        foreach (var path in new[] { "/api/ui/stats", "/api/ui/test-probe/admin" })
        {
            var answer = await SendAsync(_anonymous, HttpMethod.Get, path, key: _rootKey);

            Assert.DoesNotContain(_rootKey, answer.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("protectedPassword", answer.Body, StringComparison.OrdinalIgnoreCase);
        }
    }
}

// The twins below live in the test assembly; UiApiAccessTests registers it as an application part. They only exist to drive each base of the family.

/// <summary>An account twin (screen only, signed in).</summary>
[Route("api/ui/test-probe/account")]
public sealed class ProbeAccountApiController : AccountApiControllerBase
{
    protected override bool RequiresSignIn => true;

    [HttpPost, UiApi("ProbeAccountUiApi", "Post")]
    public ActionResult<ScreenResult<string>> Post() => Respond(ScreenResult<string>.Ok("account"));
}

/// <summary>An administrator twin.</summary>
[Route("api/ui/test-probe/admin")]
public sealed class ProbeAdminApiController : AdminApiControllerBase
{
    [HttpGet, UiApi("ProbeAdminUiApi", "Get")]
    public ActionResult<ScreenResult<string>> Get() => Respond(ScreenResult<string>.Ok("admin"));

    [HttpPost, UiApi("ProbeAdminUiApi", "Get", Handler = "Change")]
    public ActionResult<ScreenResult<string>> Post() => Respond(ScreenResult<string>.Ok("admin"));
}

/// <summary>An access twin (writes are refused while the server is read-only).</summary>
[Route("api/ui/test-probe/access")]
public sealed class ProbeAccessApiController : AccessApiControllerBase
{
    [HttpGet, UiApi("ProbeAccessUiApi", "Get")]
    public ActionResult<ScreenResult<string>> Get() => Respond(ScreenResult<string>.Ok("access"));

    [HttpPost, UiApi("ProbeAccessUiApi", "Get", Handler = "Change")]
    public ActionResult<ScreenResult<string>> Post() => Respond(ScreenResult<string>.Ok("access"));
}
