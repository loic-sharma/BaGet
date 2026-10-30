using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Security;
using BaGet.Identity.Application.Services.Sessions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// What a client without a session can do on <c>/api/ui</c> for a full single-page application: register, confirm and re-confirm the address, forgot and reset password,
/// sign in with the cookie (same origin); the origin rule that keeps other pages from scripting those endpoints; the lifetime caps of the token session; and the
/// behaviours the review of the session endpoints asked for (expired header on the session endpoints, an unrelated cookie session left alone, renewal only for the caller).
/// </summary>
public class UiApiAnonymousFlowTests : IDisposable
{
    private static readonly Dictionary<string, string> Mail = new()
    {
        { "Email:Smtp:Host", "smtp.example.com" },
        { "Email:Smtp:From", "baget@example.com" },
        { "Site:PublicUrl", "https://baget.example.com" },
    };

    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();
    private readonly Clock _clock = new();

    public UiApiAnonymousFlowTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var disposable in _disposables) disposable.Dispose();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class Clock : SystemTime
    {
        public DateTime Now { get; set; } = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public override DateTime UtcNow => Now;
    }

    private BaGetApplication Start(Dictionary<string, string>? extra = null, bool mail = false)
    {
        var settings = new Dictionary<string, string> { { "Accounts:Enabled", "true" }, { "ApiKey", "global-key" } };
        if (mail) foreach (var pair in Mail) settings[pair.Key] = pair.Value;
        foreach (var pair in extra ?? new Dictionary<string, string>()) settings[pair.Key] = pair.Value;

        var app = new BaGetApplication(_output, configOverrides: settings, configureServices: services => services.AddSingleton<SystemTime>(_clock));
        _disposables.Add(app);

        return app;
    }

    private Browser NewBrowser(BaGetApplication app)
    {
        var browser = new Browser(app);
        _disposables.Add(browser);

        return browser;
    }

    private sealed record Answer(HttpStatusCode Status, JsonNode? Json, string Body, HttpResponseHeaders Headers)
    {
        public JsonNode? Model => Json?["model"];

        public string? Error => Json?["errors"]?[""]?[0]?.GetValue<string>();

        public bool SetsCookie => Headers.Contains("Set-Cookie");
    }

    private static async Task<Answer> SendAsync(Browser browser, HttpMethod method, string path, object? body = null, string? bearer = null, string? key = null, string? xsrf = null, string? origin = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (bearer != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (key != null) request.Headers.Add("X-NuGet-ApiKey", key);
        if (xsrf != null) request.Headers.Add("X-XSRF-TOKEN", xsrf);
        if (origin != null) request.Headers.Add("Origin", origin);
        if (method != HttpMethod.Get) request.Content = new StringContent(body == null ? "{}" : System.Text.Json.JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var response = await browser.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync(Token);

        return new Answer(response.StatusCode, text.Length > 0 && text[0] == '{' ? JsonNode.Parse(text) : null, text, response.Headers);
    }

    private static async Task<string> AntiforgeryAsync(Browser browser)
        => (await SendAsync(browser, HttpMethod.Get, "/api/ui/antiforgery")).Model!["requestToken"]!.GetValue<string>();

    private static async Task<IReadOnlyList<CapturedEmail>> WaitForMailAsync(BaGetApplication app, int count)
    {
        var sender = app.Services.GetRequiredService<CapturingEmailSender>();
        for (var attempt = 0; attempt < 100 && sender.Sent.Count < count; attempt++) await Task.Delay(50, Token);

        return sender.Sent.ToList();
    }

    private static System.Collections.Specialized.NameValueCollection Query(string link) => System.Web.HttpUtility.ParseQueryString(new Uri(link).Query);

    // ----- registration

    [Fact]
    public async Task RegisteringOverHttpAnswersTheNewAccountsTokensAndSetsNoCookie()
    {
        var app = Start();
        var browser = NewBrowser(app);

        var form = await SendAsync(browser, HttpMethod.Get, "/api/ui/account/register");
        var answer = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/register", new { userName = "newbie", password = Password, confirmPassword = Password });

        Assert.Equal(HttpStatusCode.OK, form.Status);
        Assert.Equal(HttpStatusCode.OK, answer.Status);
        Assert.False(answer.SetsCookie);
        var access = answer.Model!["session"]!["accessToken"]!.GetValue<string>();
        Assert.Equal("newbie", answer.Model["session"]!["userName"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(NewBrowser(app), HttpMethod.Get, "/api/ui/account/settings", bearer: access)).Status);
        Assert.NotNull(await FindAsync(app, "newbie"));

        // Not signed in by cookie: the same browser is anonymous.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings")).Status);
    }

    [Fact]
    public async Task RegistrationFollowsTheServersRulesAndRefusesKeysAndOtherOrigins()
    {
        var closed = Start(new Dictionary<string, string> { { "Accounts:AllowRegistration", "false" } });
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(NewBrowser(closed), HttpMethod.Post, "/api/ui/account/register", new { userName = "x", password = Password, confirmPassword = Password })).Status);

        var approval = Start(new Dictionary<string, string> { { "Accounts:RequireApproval", "true" } });
        var pending = await SendAsync(NewBrowser(approval), HttpMethod.Post, "/api/ui/account/register", new { userName = "later", password = Password, confirmPassword = Password });
        Assert.Equal(HttpStatusCode.OK, pending.Status);
        Assert.True(pending.Model!["awaitingApproval"]!.GetValue<bool>());
        Assert.Null(pending.Model["session"]);

        var app = Start();
        await CreateUserAsync(app, "alice");
        string key;
        using (var scope = app.Services.CreateScope())
        {
            key = await scope.ServiceProvider.GetRequiredService<ApiKeyService>().CreateAsync((await FindAsync(app, "alice"))!.Id, "test", ApiKeyScopes.All, "*", null, null, Token);
        }

        var browser = NewBrowser(app);
        var body = new { userName = "viakey", password = Password, confirmPassword = Password };
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(browser, HttpMethod.Post, "/api/ui/account/register", body, bearer: key)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(browser, HttpMethod.Post, "/api/ui/account/register", body, origin: "https://evil.example")).Status);
        Assert.Null(await FindAsync(app, "viakey"));
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(browser, HttpMethod.Post, "/api/ui/account/register", body, origin: "http://localhost")).Status);
    }

    [Fact]
    public async Task AnEmailAddressIsMailedOncePerWindowButARefusedAttemptNeverUsesUpItsAllowance()
    {
        var app = Start(mail: true);
        await CreateUserAsync(app, "taken", email: "taken@example.com");
        var browser = NewBrowser(app);

        // A refused attempt (name taken, weak password) charges nothing against the address: the retry works.
        var refused = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/register", new { userName = "taken", email = "same@example.com", password = Password, confirmPassword = Password });
        var weak = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/register", new { userName = "one", email = "same@example.com", password = "a", confirmPassword = "a" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.Equal(HttpStatusCode.BadRequest, weak.Status);

        var first = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/register", new { userName = "one", email = "same@example.com", password = Password, confirmPassword = Password });
        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.True(first.Model!["confirmationSent"]!.GetValue<bool>());
        Assert.Single(await WaitForMailAsync(app, 1));

        // Once an account exists for it, the address is mailed once per window: a second account is created, no second mail is sent.
        var second = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/register", new { userName = "two", email = "same@example.com", password = Password, confirmPassword = Password });
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.NotNull(await FindAsync(app, "two"));
        await Task.Delay(300, Token);
        Assert.Single(app.Services.GetRequiredService<CapturingEmailSender>().Sent);
    }

    // ----- forgot / reset / confirm / resend

    [Fact]
    public async Task ForgotPasswordAnswersTheSameForAnUnknownAccountAndMailsOneLinkPerWindow()
    {
        var app = Start(mail: true);
        await CreateUserAsync(app, "alice", email: "alice@example.com");
        var browser = NewBrowser(app);

        var known = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/forgotpassword", new { identifier = "alice" });
        var unknown = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/forgotpassword", new { identifier = "nobody" });
        var again = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/forgotpassword", new { identifier = "alice@example.com" });

        foreach (var answer in new[] { known, unknown, again })
        {
            Assert.Equal(HttpStatusCode.OK, answer.Status);
            Assert.True(answer.Model!["sent"]!.GetValue<bool>());
            Assert.False(answer.SetsCookie);
        }

        Assert.Equal(known.Json!["status"]!.GetValue<string>(), unknown.Json!["status"]!.GetValue<string>());
        var mails = await WaitForMailAsync(app, 1);
        await Task.Delay(300, Token);
        Assert.Single(app.Services.GetRequiredService<CapturingEmailSender>().Sent);
        Assert.Equal("reset-link", mails[0].Kind);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(browser, HttpMethod.Post, "/api/ui/account/forgotpassword", new { identifier = "alice" }, origin: "https://evil.example")).Status);
    }

    [Fact]
    public async Task ThePasswordIsResetFromTheMailedLinkWithoutACookieAndABadLinkIsTheSameWhoeverItNamed()
    {
        var app = Start(mail: true);
        await CreateUserAsync(app, "alice", email: "alice@example.com");
        var browser = NewBrowser(app);
        await SendAsync(browser, HttpMethod.Post, "/api/ui/account/forgotpassword", new { identifier = "alice" });
        var link = (await WaitForMailAsync(app, 1))[0].Payload;
        var query = Query(link);
        var userId = query["userId"]!;
        var code = query["code"]!;

        var shown = await SendAsync(browser, HttpMethod.Get, $"/api/ui/account/resetpassword?userId={Uri.EscapeDataString(userId)}&code={Uri.EscapeDataString(code)}");
        Assert.Equal(HttpStatusCode.OK, shown.Status);

        var unknownUser = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/resetpassword", new { userId = "nobody", code, password = "N3w-Passw0rd!", confirmPassword = "N3w-Passw0rd!" });
        var badCode = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/resetpassword", new { userId, code = "AAAA", password = "N3w-Passw0rd!", confirmPassword = "N3w-Passw0rd!" });
        Assert.Equal(HttpStatusCode.BadRequest, unknownUser.Status);
        Assert.Equal(unknownUser.Error, badCode.Error);

        var reset = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/resetpassword", new { userId, code, password = "N3w-Passw0rd!", confirmPassword = "N3w-Passw0rd!" });
        Assert.Equal(HttpStatusCode.OK, reset.Status);
        Assert.False(reset.SetsCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session", new { userName = "alice", password = Password })).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session", new { userName = "alice", password = "N3w-Passw0rd!" })).Status);
    }

    [Fact]
    public async Task AConfirmationLinkCanBeSentAgainOncePerWindowAndTheAnswerNeverTellsWhoHasAnAccount()
    {
        var app = Start(mail: true);
        await CreateUserAsync(app, "bob", email: "bob@example.com");
        await CreateUserAsync(app, "carol", email: "carol@example.com");
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
            var bob = (await users.FindByNameAsync("bob"))!;
            bob.EmailConfirmed = false;
            await users.UpdateAsync(bob);
        }

        var browser = NewBrowser(app);
        var unconfirmed = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/resend-confirmation", new { identifier = "bob" });
        var confirmed = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/resend-confirmation", new { identifier = "carol" });
        var unknown = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/resend-confirmation", new { identifier = "nobody" });
        var again = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/resend-confirmation", new { identifier = "bob" });

        foreach (var answer in new[] { unconfirmed, confirmed, unknown, again })
        {
            Assert.Equal(HttpStatusCode.OK, answer.Status);
            Assert.True(answer.Model!["sent"]!.GetValue<bool>());
        }

        var mails = await WaitForMailAsync(app, 1);
        await Task.Delay(300, Token);
        var sent = Assert.Single(app.Services.GetRequiredService<CapturingEmailSender>().Sent);
        Assert.Equal(("confirmation", "bob@example.com"), (sent.Kind, sent.To));

        // The link confirms the address; a made-up one is the same harmless answer.
        var query = Query(mails[0].Payload);
        var confirm = await SendAsync(browser, HttpMethod.Get, $"/api/ui/account/confirmemail?userId={Uri.EscapeDataString(query["userId"]!)}&code={Uri.EscapeDataString(query["code"]!)}");
        var bogus = await SendAsync(browser, HttpMethod.Get, "/api/ui/account/confirmemail?userId=nobody&code=AAAA");
        Assert.True(confirm.Model!["succeeded"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.OK, bogus.Status);
        Assert.False(bogus.Model!["succeeded"]!.GetValue<bool>());
        Assert.True((await FindAsync(app, "bob"))!.EmailConfirmed);
    }

    [Fact]
    public async Task WithoutTheScreensEmailLinksPointAtTheApplicationsRoutesWithTheSameQueryValues()
    {
        var app = Start(new Dictionary<string, string> { { "Frontend:Mvc", "false" }, { "Frontend:SpaLinks", "true" }, { "Frontend:SpaBasePath", "/spa" } }, mail: true);
        await CreateUserAsync(app, "alice", email: "alice@example.com");
        var browser = NewBrowser(app);

        await SendAsync(browser, HttpMethod.Post, "/api/ui/account/forgotpassword", new { identifier = "alice" });
        await SendAsync(browser, HttpMethod.Post, "/api/ui/account/register", new { userName = "newbie", email = "new@example.com", password = Password, confirmPassword = Password });
        var mails = await WaitForMailAsync(app, 2);

        var reset = mails.Single(m => m.Kind == "reset-link").Payload;
        var confirmation = mails.Single(m => m.Kind == "confirmation").Payload;
        Assert.StartsWith("https://baget.example.com/spa/reset-password?userId=", reset, StringComparison.Ordinal);
        Assert.NotNull(Query(reset)["code"]);
        Assert.StartsWith("https://baget.example.com/spa/confirm-email?userId=", confirmation, StringComparison.Ordinal);
        Assert.NotNull(Query(confirmation)["code"]);
    }

    [Fact]
    public async Task AnApplicationHostedElsewhereIsLinkedByItsOwnAddressAndNoLinkEverPointsAtAMissingPage()
    {
        var app = Start(new Dictionary<string, string> { { "Frontend:Mvc", "false" }, { "Frontend:SpaUrl", "https://app.example.com/" } }, mail: true);
        await CreateUserAsync(app, "alice", email: "alice@example.com");
        await SendAsync(NewBrowser(app), HttpMethod.Post, "/api/ui/account/forgotpassword", new { identifier = "alice" });
        Assert.StartsWith("https://app.example.com/reset-password?userId=", (await WaitForMailAsync(app, 1))[0].Payload, StringComparison.Ordinal);

        // No screens and no application to link to: the host does not start with mail configured, instead of mailing links to a 404.
        var dead = Record.Exception(() => Start(new Dictionary<string, string> { { "Frontend:Mvc", "false" } }, mail: true).Services);
        Assert.NotNull(dead);
        Assert.Contains("Frontend:SpaUrl", dead!.ToString(), StringComparison.Ordinal);

        // Without mail nothing is linked, and the host starts.
        Assert.Null(Record.Exception(() => Start(new Dictionary<string, string> { { "Frontend:Mvc", "false" } }).Services));

        var invalid = Record.Exception(() => Start(new Dictionary<string, string> { { "Frontend:SpaUrl", "ftp://app.example.com" } }).Services);
        Assert.NotNull(invalid);
        Assert.Contains("Frontend:SpaUrl", invalid!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithTheScreensEmailLinksStayOnThem()
    {
        var app = Start(mail: true);
        await CreateUserAsync(app, "alice", email: "alice@example.com");

        await SendAsync(NewBrowser(app), HttpMethod.Post, "/api/ui/account/forgotpassword", new { identifier = "alice" });

        Assert.StartsWith("https://baget.example.com/account/resetpassword?userId=", (await WaitForMailAsync(app, 1))[0].Payload, StringComparison.Ordinal);
    }

    // ----- the origin rule

    [Fact]
    public async Task OnlyThisOriginOrAListedOneMayCallTheSessionEndpointsFromABrowser()
    {
        var without = Start();
        await CreateUserAsync(without, "alice");
        var browser = NewBrowser(without);
        var credentials = new { userName = "alice", password = Password };

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session", credentials, origin: "https://evil.example")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(browser, HttpMethod.Get, "/api/ui/antiforgery", origin: "https://evil.example")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session", credentials, origin: "https://baget.example.com")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session/refresh", new { refreshToken = "bgs.x" }, origin: "null")).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session", credentials, origin: "http://localhost")).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session", credentials)).Status);

        var listed = Start(new Dictionary<string, string> { { "Cors:AllowedOrigins", "https://spa.example.com" } });
        await CreateUserAsync(listed, "alice");
        var second = NewBrowser(listed);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(second, HttpMethod.Post, "/api/ui/session", credentials, origin: "https://spa.example.com")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(second, HttpMethod.Post, "/api/ui/session", credentials, origin: "https://evil.example")).Status);

        // The cookie endpoints are for this origin alone, listed or not.
        var token = await AntiforgeryAsync(second);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(second, HttpMethod.Post, "/api/ui/session/cookie", credentials, xsrf: token, origin: "https://spa.example.com")).Status);
    }

    [Fact]
    public async Task TheOriginOfThePublicUrlIsThisServersOriginBehindATlsProxyThatIsNotTrusted()
    {
        // The request arrives as http://localhost (X-Forwarded-Proto not trusted), while the site is https://baget.example.com.
        var app = Start(new Dictionary<string, string> { { "Site:PublicUrl", "https://baget.example.com" } });
        await CreateUserAsync(app, "alice");
        var browser = NewBrowser(app);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session", new { userName = "alice", password = Password }, origin: "https://baget.example.com")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session", new { userName = "alice", password = Password }, origin: "https://evil.example")).Status);
    }

    // ----- expired header, lifetime cap

    [Fact]
    public async Task AnExpiredOrInvalidTokenInTheHeaderDoesNotStopTheEndpointsThatGiveASessionOut()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        var browser = NewBrowser(app);
        var tokens = (await SendAsync(browser, HttpMethod.Post, "/api/ui/session", new { userName = "alice", password = Password })).Model!;
        var access = tokens["accessToken"]!.GetValue<string>();
        var refresh = tokens["refreshToken"]!.GetValue<string>();

        _clock.Now = _clock.Now.AddMinutes(20);

        // The expired access token still rides on every request of such a client.
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings", bearer: access)).Status);
        var refreshed = await SendAsync(browser, HttpMethod.Post, "/api/ui/session/refresh", new { refreshToken = refresh }, bearer: access);
        Assert.Equal(HttpStatusCode.OK, refreshed.Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session", new { userName = "alice", password = Password }, bearer: access)).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(browser, HttpMethod.Get, "/api/ui/antiforgery", bearer: access)).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session/sign-out", new { }, bearer: "bgs.garbage")).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings", bearer: refreshed.Model!["accessToken"]!.GetValue<string>())).Status);
    }

    [Fact]
    public async Task ASessionEndsAtItsAbsoluteLifetimeHoweverOftenItIsRefreshed()
    {
        var app = Start(new Dictionary<string, string> { { "Accounts:SessionMaxLifetime", "00:30:00" } });
        await CreateUserAsync(app, "alice");
        var browser = NewBrowser(app);
        var first = (await SendAsync(browser, HttpMethod.Post, "/api/ui/session", new { userName = "alice", password = Password })).Model!;
        var signedIn = _clock.Now;

        // Neither token outlives the cap.
        Assert.Equal(signedIn.AddMinutes(15), first["expiresAt"]!.GetValue<DateTime>().ToUniversalTime());
        Assert.Equal(signedIn.AddMinutes(30), first["refreshExpiresAt"]!.GetValue<DateTime>().ToUniversalTime());

        _clock.Now = signedIn.AddMinutes(20);
        var second = await SendAsync(browser, HttpMethod.Post, "/api/ui/session/refresh", new { refreshToken = first["refreshToken"]!.GetValue<string>() });
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Equal(signedIn.AddMinutes(30), second.Model!["expiresAt"]!.GetValue<DateTime>().ToUniversalTime());
        Assert.Equal(signedIn.AddMinutes(30), second.Model["refreshExpiresAt"]!.GetValue<DateTime>().ToUniversalTime());

        _clock.Now = signedIn.AddMinutes(31);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session/refresh", new { refreshToken = second.Model["refreshToken"]!.GetValue<string>() })).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings", bearer: second.Model["accessToken"]!.GetValue<string>())).Status);
    }

    // ----- the cookie, signed in through the API

    [Fact]
    public async Task TheCookieSignInNeedsASameOriginAntiforgeryTokenAndSignsInWithTheCookieOnly()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        var browser = NewBrowser(app);
        var credentials = new { userName = "alice", password = Password };

        // No login CSRF: without the token of this visitor nothing happens.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session/cookie", credentials)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session/cookie", credentials, xsrf: "not-a-token")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session/cookie", credentials, xsrf: await AntiforgeryAsync(NewBrowser(app)))).Status);

        var anonymousToken = await AntiforgeryAsync(browser);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session/cookie", new { userName = "alice", password = "nope" }, xsrf: anonymousToken)).Status);

        var signedIn = await SendAsync(browser, HttpMethod.Post, "/api/ui/session/cookie", credentials, xsrf: anonymousToken);
        Assert.Equal(HttpStatusCode.OK, signedIn.Status);
        Assert.True(signedIn.SetsCookie);
        Assert.Equal("alice", signedIn.Model!["userName"]!.GetValue<string>());
        Assert.Null(signedIn.Model["accessToken"]);

        // The cookie is a session now, with a fresh token bound to the user.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings")).Status);
        var userToken = await AntiforgeryAsync(browser);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings", xsrf: userToken)).Status);
    }

    [Fact]
    public async Task TheCookieSignInHonoursTheLockoutAndTheSecondFactor()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        await CreateUserAsync(app, "locked");
        var (key, _) = await UiApiSessionTests.EnableTwoFactorAsync(app, "alice");
        var browser = NewBrowser(app);
        var token = await AntiforgeryAsync(browser);

        var first = await SendAsync(browser, HttpMethod.Post, "/api/ui/session/cookie", new { userName = "alice", password = Password }, xsrf: token);
        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.True(first.Model!["twoFactorRequired"]!.GetValue<bool>());
        Assert.False(first.SetsCookie);
        var ticket = first.Model["twoFactorTicket"]!.GetValue<string>();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session/cookie", new { ticket, code = "000000" }, xsrf: token)).Status);
        var second = await SendAsync(browser, HttpMethod.Post, "/api/ui/session/cookie", new { ticket, code = UiApiSessionTests.Totp(key) }, xsrf: token);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.True(second.SetsCookie);

        var other = NewBrowser(app);
        var otherToken = await AntiforgeryAsync(other);
        for (var attempt = 0; attempt < 5; attempt++) await SendAsync(other, HttpMethod.Post, "/api/ui/session/cookie", new { userName = "locked", password = "nope" }, xsrf: otherToken);
        var locked = await SendAsync(other, HttpMethod.Post, "/api/ui/session/cookie", new { userName = "locked", password = Password }, xsrf: otherToken);
        Assert.Equal(HttpStatusCode.Forbidden, locked.Status);
        Assert.False(locked.SetsCookie);
    }

    [Fact]
    public async Task TheCookieSignOutNeedsTheCookieSessionAndEndsIt()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        var browser = NewBrowser(app);
        await SendAsync(browser, HttpMethod.Post, "/api/ui/session/cookie", new { userName = "alice", password = Password }, xsrf: await AntiforgeryAsync(browser));
        var token = await AntiforgeryAsync(browser);

        var access = (await SendAsync(NewBrowser(app), HttpMethod.Post, "/api/ui/session", new { userName = "alice", password = Password })).Model!["accessToken"]!.GetValue<string>();
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session/cookie/sign-out", bearer: access)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session/cookie/sign-out")).Status);

        var out_ = await SendAsync(browser, HttpMethod.Post, "/api/ui/session/cookie/sign-out", xsrf: token);
        Assert.Equal(HttpStatusCode.OK, out_.Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings", xsrf: await AntiforgeryAsync(browser))).Status);
    }

    // ----- review points of the session endpoints

    [Fact]
    public async Task TheExternalSignInLeavesAnUnrelatedCookieSessionAloneAndCleansItsOwnCookie()
    {
        var app = new BaGetApplication(
            _output,
            configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "true" }, { "ApiKey", "" } },
            configureServices: services => services.AddTransient<IStartupFilter, ExternalCookieFilter>().AddSingleton<SystemTime>(_clock));
        _disposables.Add(app);
        await CreateUserAsync(app, "bob");
        var browser = NewBrowser(app);
        await AdminTestSupport.SignInAsync(browser, "bob");
        Assert.Contains("account/settings", await browser.GetPageAsync(""), StringComparison.Ordinal);

        using (await browser.GetAsync("test/external?sub=s1&email=alice%40example.com&name=alice&verified=true")) { }
        using var callback = await browser.GetAsync("api/ui/session/external-callback?returnUrl=%2Fapp%2F");
        Assert.Contains("#exchange_token=", callback.Headers.Location!.ToString(), StringComparison.Ordinal);

        // bob's browser session is untouched (and was not replaced by alice's) ...
        Assert.Contains("account/settings", await browser.GetPageAsync(""), StringComparison.Ordinal);
        using var settings = await browser.GetAsync("account/settings");
        Assert.Contains("bob", await settings.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);

        // ... and the external cookie is gone: a second callback has nothing to complete.
        using var again = await browser.GetAsync("api/ui/session/external-callback?returnUrl=%2Fapp%2F");
        Assert.Contains("#error=", again.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASignInOnlyRenewsTheTokensOfTheCallersOwnAccount()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        await CreateUserAsync(app, "bob");
        var alice = (await FindAsync(app, "alice"))!;
        var bob = (await FindAsync(app, "bob"))!;

        using var scope = app.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Items[AccountConfirmation.SessionAuthenticatedItem] = _clock.Now;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, alice.Id) }, "test"));
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
        var manager = scope.ServiceProvider.GetRequiredService<SignInManager<BaGetUser>>();

        await manager.RefreshSignInAsync(bob);
        Assert.False(context.Response.Headers.ContainsKey(SessionHeaders.AccessToken));

        await manager.RefreshSignInAsync(alice);
        Assert.True(context.Response.Headers.ContainsKey(SessionHeaders.AccessToken));
    }

    [Theory]
    [InlineData("/app/", "/app", true)]
    [InlineData("/app", "/app", true)]
    [InlineData("/app/users/1?x=1", "/app/", true)]
    [InlineData("/application", "/app", false)]
    [InlineData("/app/../account/manage", "/app", false)]
    [InlineData("/app/%2e%2e/account/manage", "/app", false)]
    [InlineData("/app/%2E%2E/account/manage", "/app", false)]
    [InlineData("/app/./x", "/app", true)]
    [InlineData("/app/x/../y", "/app", true)]
    [InlineData("/app/x/../../account", "/app", false)]
    [InlineData("/app%2fx", "/app", false)]
    [InlineData("https://baget.example.com/app/../account", "/app", false)]
    [InlineData("/other", "/app", false)]
    [InlineData("/admin/users", "/app", false)]
    [InlineData("https://baget.example.com/app/x", "/app", true)]
    [InlineData("https://baget.example.com/other", "/app", false)]
    [InlineData("https://spa.example.com/anything", "/app", true)]
    [InlineData("/anything", "/", true)]
    public void SameOriginReturnAddressesMustLieUnderTheApplicationsBasePath(string returnUrl, string basePath, bool allowed)
    {
        var origins = SessionReturnUrls.Origins("https://spa.example.com");

        Assert.Equal(allowed, SessionReturnUrls.IsAllowed(returnUrl, "https://baget.example.com", origins, basePath));
    }
}
