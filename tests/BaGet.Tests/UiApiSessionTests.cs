using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Security;
using BaGet.Identity.Application.Services.Sessions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// The interactive session of <c>/api/ui/session</c> (tokens for a single-page application): sign-in rules, the second factor, refresh, expiry,
/// revocation by the security stamp, where a token is accepted (only <c>/api/ui</c>) and how a session is judged next to a key, the cookie and the global key.
/// </summary>
public class UiApiSessionTests : IDisposable
{
    private const string GlobalKey = "global-key";

    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();
    private readonly MutableClock _clock = new();

    public UiApiSessionTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var disposable in _disposables) disposable.Dispose();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>The mocked clock of the harness, made movable.</summary>
    private sealed class MutableClock : SystemTime
    {
        public DateTime Now { get; set; } = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public override DateTime UtcNow => Now;
    }

    private BaGetApplication Start(Dictionary<string, string>? extra = null)
    {
        var settings = new Dictionary<string, string> { { "Accounts:Enabled", "true" }, { "ApiKey", GlobalKey } };
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
    }

    private static async Task<Answer> SendAsync(Browser browser, HttpMethod method, string path, object? body = null, string? bearer = null, string? key = null, string? xsrf = null, string? confirm = null, string? origin = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (bearer != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (key != null) request.Headers.Add("X-NuGet-ApiKey", key);
        if (xsrf != null) request.Headers.Add("X-XSRF-TOKEN", xsrf);
        if (confirm != null) request.Headers.Add("X-Confirm-Password", confirm);
        if (origin != null) request.Headers.Add("Origin", origin);
        if (method != HttpMethod.Get) request.Content = new StringContent(body == null ? "{}" : System.Text.Json.JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var response = await browser.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync(Token);

        return new Answer(response.StatusCode, text.Length > 0 && text[0] == '{' ? JsonNode.Parse(text) : null, text, response.Headers);
    }

    private static Task<Answer> SignInAsync(Browser browser, string user, string password = Password)
        => SendAsync(browser, HttpMethod.Post, "/api/ui/session", new { userName = user, password });

    private static async Task<Answer> SignedInAsync(Browser browser, string user)
    {
        var answer = await SignInAsync(browser, user);
        Assert.Equal(HttpStatusCode.OK, answer.Status);

        return answer;
    }

    private static async Task UpdateUserAsync(BaGetApplication app, string name, Action<UserManager<BaGetUser>, BaGetUser> change)
    {
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
        var user = (await users.FindByNameAsync(name))!;
        change(users, user);
        var result = await users.UpdateAsync(user);
        Assert.True(result.Succeeded);
    }

    // ----- sign-in

    [Fact]
    public async Task SigningInAnswersTokensThatAuthenticateTheUiApiWithTheConfiguredLifetimes()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        var browser = NewBrowser(app);

        var answer = await SignInAsync(browser, "alice");

        Assert.Equal(HttpStatusCode.OK, answer.Status);
        Assert.Equal("Ok", answer.Json!["status"]!.GetValue<string>());
        Assert.Equal("alice", answer.Model!["userName"]!.GetValue<string>());
        var access = answer.Model["accessToken"]!.GetValue<string>();
        var refresh = answer.Model["refreshToken"]!.GetValue<string>();
        Assert.StartsWith(SessionTokenService.Prefix, access, StringComparison.Ordinal);
        Assert.StartsWith(SessionTokenService.Prefix, refresh, StringComparison.Ordinal);
        Assert.Equal(_clock.Now.AddMinutes(15), answer.Model["expiresAt"]!.GetValue<DateTime>().ToUniversalTime());
        Assert.Equal(_clock.Now.AddDays(14), answer.Model["refreshExpiresAt"]!.GetValue<DateTime>().ToUniversalTime());
        Assert.Contains("no-store", answer.Headers.CacheControl!.ToString(), StringComparison.Ordinal);

        var settings = await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings", bearer: access);
        Assert.Equal(HttpStatusCode.OK, settings.Status);

        // No cookie was set: the same browser without the header is anonymous.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings")).Status);
    }

    [Fact]
    public async Task TheLifetimesComeFromTheAccountsSettings()
    {
        var app = Start(new Dictionary<string, string> { { "Accounts:SessionTokenLifetime", "00:02:00" }, { "Accounts:SessionRefreshLifetime", "2.00:00:00" } });
        await CreateUserAsync(app, "alice");

        var answer = await SignedInAsync(NewBrowser(app), "alice");

        Assert.Equal(_clock.Now.AddMinutes(2), answer.Model!["expiresAt"]!.GetValue<DateTime>().ToUniversalTime());
        Assert.Equal(_clock.Now.AddDays(2), answer.Model["refreshExpiresAt"]!.GetValue<DateTime>().ToUniversalTime());
    }

    [Fact]
    public async Task AWrongPasswordAndAnUnknownUserAreTheSame401AndAnEmptyFormIs400()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        var browser = NewBrowser(app);

        var wrong = await SignInAsync(browser, "alice", "nope");
        var unknown = await SignInAsync(browser, "nobody", "nope");

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.Status);
        Assert.Equal(wrong.Error, unknown.Error);
        Assert.Null(wrong.Model);

        var empty = await SendAsync(browser, HttpMethod.Post, "/api/ui/session", new { });
        Assert.Equal(HttpStatusCode.BadRequest, empty.Status);
        Assert.NotNull(empty.Json!["errors"]!["UserName"]);
    }

    [Fact]
    public async Task WrongPasswordsAreCountedAndTheAccountIsLockedOutFirst()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        var browser = NewBrowser(app);

        for (var attempt = 0; attempt < 4; attempt++) Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(browser, "alice", "nope")).Status);

        // The fifth failure locks the account; from then on even the right password is refused, and the answer says why.
        Assert.Equal(HttpStatusCode.Forbidden, (await SignInAsync(browser, "alice", "nope")).Status);
        var locked = await SignInAsync(browser, "alice");
        Assert.Equal(HttpStatusCode.Forbidden, locked.Status);
        Assert.Contains("locked", locked.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnAccountAwaitingApprovalAnswersOnlyToTheRightPassword()
    {
        var app = Start(new Dictionary<string, string> { { "Accounts:RequireApproval", "true" } });
        await CreateUserAsync(app, "alice");
        await UpdateUserAsync(app, "alice", (_, user) => user.PendingApproval = true);
        var browser = NewBrowser(app);

        var wrong = await SignInAsync(browser, "alice", "nope");
        var right = await SignInAsync(browser, "alice");

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.Status);
        Assert.Equal(HttpStatusCode.Forbidden, right.Status);
        Assert.Contains("approved", right.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(right.Model);
    }

    [Fact]
    public async Task AnUnconfirmedEmailIsRefusedOnlyToTheRightPasswordWhenMailIsConfigured()
    {
        var app = Start(new Dictionary<string, string>
        {
            { "Email:Smtp:Host", "smtp.example.com" },
            { "Email:Smtp:From", "baget@example.com" },
            { "Site:PublicUrl", "https://baget.example.com" },
        });
        await CreateUserAsync(app, "alice", email: "alice@example.com");
        await UpdateUserAsync(app, "alice", (_, user) => user.EmailConfirmed = false);
        var browser = NewBrowser(app);

        var wrong = await SignInAsync(browser, "alice", "nope");
        var right = await SignInAsync(browser, "alice");

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.Status);
        Assert.Equal(HttpStatusCode.Forbidden, right.Status);
        Assert.Contains("Confirm your email", right.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSessionEndpointsAre404WhileAccountsAreOff()
    {
        using var app = new BaGetApplication(_output);
        using var browser = new Browser(app);

        Assert.Equal(HttpStatusCode.NotFound, (await SignInAsync(browser, "alice")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session/refresh", new { refreshToken = "bgs.x" })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(browser, HttpMethod.Get, "/api/ui/antiforgery")).Status);
    }

    // ----- second factor

    internal static string Totp(string base32Key)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = string.Concat(base32Key.TrimEnd('=').ToUpperInvariant().Select(c => Convert.ToString(alphabet.IndexOf(c), 2).PadLeft(5, '0')));
        var key = Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2)).ToArray();
        var counter = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0xf;
        var number = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];

        return (number % 1_000_000).ToString("D6");
    }

    internal static async Task<(string Key, string[] RecoveryCodes)> EnableTwoFactorAsync(BaGetApplication app, string name)
    {
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
        var user = (await users.FindByNameAsync(name))!;
        await users.ResetAuthenticatorKeyAsync(user);
        var key = (await users.GetAuthenticatorKeyAsync(user))!;
        await users.SetTwoFactorEnabledAsync(user, true);
        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 3))!.ToArray();

        return (key, codes);
    }

    [Fact]
    public async Task AnAccountWithTwoFactorGetsATicketThatOnlyACodeTurnsIntoTokens()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        var (key, _) = await EnableTwoFactorAsync(app, "alice");
        var browser = NewBrowser(app);

        var first = await SignedInAsync(browser, "alice");

        Assert.True(first.Model!["twoFactorRequired"]!.GetValue<bool>());
        Assert.Null(first.Model["accessToken"]);
        Assert.Null(first.Model["refreshToken"]);
        var ticket = first.Model["twoFactorTicket"]!.GetValue<string>();
        Assert.Equal(_clock.Now.AddMinutes(5), first.Model["twoFactorTicketExpiresAt"]!.GetValue<DateTime>().ToUniversalTime());

        // The ticket authenticates nothing.
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings", bearer: ticket)).Status);

        var second = await SendAsync(browser, HttpMethod.Post, "/api/ui/session/two-factor", new { ticket, code = Totp(key) });

        Assert.Equal(HttpStatusCode.OK, second.Status);
        var access = second.Model!["accessToken"]!.GetValue<string>();
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings", bearer: access)).Status);

        // An access token is not a ticket.
        var swapped = await SendAsync(browser, HttpMethod.Post, "/api/ui/session/two-factor", new { ticket = access, code = Totp(key) });
        Assert.Equal(HttpStatusCode.Unauthorized, swapped.Status);
    }

    [Fact]
    public async Task AWrongCodeIsCountedAndTooManyLockTheAccount()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        var (key, _) = await EnableTwoFactorAsync(app, "alice");
        var browser = NewBrowser(app);
        var ticket = (await SignedInAsync(browser, "alice")).Model!["twoFactorTicket"]!.GetValue<string>();

        for (var attempt = 0; attempt < 4; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session/two-factor", new { ticket, code = "000000" })).Status);
        }

        var fifth = await SendAsync(browser, HttpMethod.Post, "/api/ui/session/two-factor", new { ticket, code = "000000" });
        Assert.Equal(HttpStatusCode.Forbidden, fifth.Status);
        Assert.Contains("locked", fifth.Error, StringComparison.OrdinalIgnoreCase);

        // Locked out: not even the right code (nor the password) gets in, and the answer says why.
        var afterLock = await SendAsync(browser, HttpMethod.Post, "/api/ui/session/two-factor", new { ticket, code = Totp(key) });
        Assert.Equal(HttpStatusCode.Forbidden, afterLock.Status);
        Assert.Contains("locked", afterLock.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Forbidden, (await SignInAsync(browser, "alice")).Status);
    }

    [Fact]
    public async Task ARecoveryCodeCompletesTheSignInOnceAndTheTicketExpires()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        var (_, codes) = await EnableTwoFactorAsync(app, "alice");
        var browser = NewBrowser(app);
        var ticket = (await SignedInAsync(browser, "alice")).Model!["twoFactorTicket"]!.GetValue<string>();

        var first = await SendAsync(browser, HttpMethod.Post, "/api/ui/session/recovery-code", new { ticket, recoveryCode = codes[0] });
        var again = await SendAsync(browser, HttpMethod.Post, "/api/ui/session/recovery-code", new { ticket, recoveryCode = codes[0] });

        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.NotNull(first.Model!["accessToken"]);
        Assert.Equal(HttpStatusCode.Unauthorized, again.Status);

        _clock.Now = _clock.Now.AddMinutes(6);
        var late = await SendAsync(browser, HttpMethod.Post, "/api/ui/session/recovery-code", new { ticket, recoveryCode = codes[1] });
        Assert.Equal(HttpStatusCode.Unauthorized, late.Status);
    }

    // ----- refresh, expiry, revocation

    [Fact]
    public async Task RefreshingIssuesANewPairAndKeepsTheAuthenticationTime()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        var browser = NewBrowser(app);
        var signedInAt = _clock.Now;
        var first = await SignedInAsync(browser, "alice");
        var refresh = first.Model!["refreshToken"]!.GetValue<string>();

        _clock.Now = _clock.Now.AddMinutes(20);
        var second = await SendAsync(browser, HttpMethod.Post, "/api/ui/session/refresh", new { refreshToken = refresh });

        Assert.Equal(HttpStatusCode.OK, second.Status);
        var newAccess = second.Model!["accessToken"]!.GetValue<string>();
        var newRefresh = second.Model["refreshToken"]!.GetValue<string>();
        Assert.NotEqual(refresh, newRefresh);
        Assert.NotEqual(first.Model["accessToken"]!.GetValue<string>(), newAccess);
        Assert.Equal(_clock.Now.AddMinutes(15), second.Model["expiresAt"]!.GetValue<DateTime>().ToUniversalTime());
        Assert.Equal(_clock.Now.AddDays(14), second.Model["refreshExpiresAt"]!.GetValue<DateTime>().ToUniversalTime());
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings", bearer: newAccess)).Status);

        using var scope = app.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<SessionAuthenticator>().AuthenticateAsync(SessionTokenKind.Refresh, newRefresh);
        Assert.Equal(signedInAt, session!.AuthenticatedAt);
    }

    [Fact]
    public async Task TokensExpireAndEachKindOnlyServesItsOwnPurpose()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        var browser = NewBrowser(app);
        var tokens = (await SignedInAsync(browser, "alice")).Model!;
        var access = tokens["accessToken"]!.GetValue<string>();
        var refresh = tokens["refreshToken"]!.GetValue<string>();

        // A refresh token does not authenticate, an access token does not refresh.
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings", bearer: refresh)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session/refresh", new { refreshToken = access })).Status);

        _clock.Now = _clock.Now.AddMinutes(16);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings", bearer: access)).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session/refresh", new { refreshToken = refresh })).Status);

        _clock.Now = _clock.Now.AddDays(15);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session/refresh", new { refreshToken = refresh })).Status);
    }

    [Fact]
    public async Task ATamperedTokenIsInvalid()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        var browser = NewBrowser(app);
        var access = (await SignedInAsync(browser, "alice")).Model!["accessToken"]!.GetValue<string>();
        var tampered = access.Substring(0, access.Length - 4) + (access.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA");

        var answer = await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings", bearer: tampered);

        Assert.Equal(HttpStatusCode.Unauthorized, answer.Status);
        Assert.Equal("SignIn", answer.Json!["status"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings", bearer: "bgs.garbage")).Status);
    }

    [Fact]
    public async Task ChangingThePasswordRevokesEveryToken()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        var browser = NewBrowser(app);
        var tokens = (await SignedInAsync(browser, "alice")).Model!;
        var access = tokens["accessToken"]!.GetValue<string>();
        var refresh = tokens["refreshToken"]!.GetValue<string>();
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings", bearer: access)).Status);

        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
            var user = (await users.FindByNameAsync("alice"))!;
            Assert.True((await users.ChangePasswordAsync(user, Password, "An0ther-Passw0rd!")).Succeeded);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings", bearer: access)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session/refresh", new { refreshToken = refresh })).Status);
    }

    [Fact]
    public async Task SigningOutEverywhereEndsEveryTokenAndPlainSignOutRevokesNothing()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        var browser = NewBrowser(app);
        var first = (await SignedInAsync(browser, "alice")).Model!;
        var second = (await SignedInAsync(browser, "alice")).Model!;
        var firstAccess = first["accessToken"]!.GetValue<string>();
        var secondAccess = second["accessToken"]!.GetValue<string>();

        var plain = await SendAsync(browser, HttpMethod.Post, "/api/ui/session/sign-out", new { }, bearer: firstAccess);
        Assert.Equal(HttpStatusCode.OK, plain.Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings", bearer: firstAccess)).Status);

        var everywhere = await SendAsync(browser, HttpMethod.Post, "/api/ui/session/sign-out", new { everywhere = true }, bearer: firstAccess);
        Assert.Equal(HttpStatusCode.OK, everywhere.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings", bearer: firstAccess)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings", bearer: secondAccess)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session/refresh", new { refreshToken = second["refreshToken"]!.GetValue<string>() })).Status);
    }

    [Fact]
    public async Task SigningOutEverywhereWithTheRefreshTokenAloneWorksAndWithNothingIs401()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        var browser = NewBrowser(app);
        var tokens = (await SignedInAsync(browser, "alice")).Model!;

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session/sign-out", new { everywhere = true })).Status);

        var answer = await SendAsync(browser, HttpMethod.Post, "/api/ui/session/sign-out", new { everywhere = true, refreshToken = tokens["refreshToken"]!.GetValue<string>() });
        Assert.Equal(HttpStatusCode.OK, answer.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings", bearer: tokens["accessToken"]!.GetValue<string>())).Status);
    }

    [Fact]
    public async Task APendingOrDeletedAccountsTokensStopWorkingButALockedOutAccountKeepsItsSessionLikeACookie()
    {
        var app = Start();
        foreach (var name in new[] { "locked", "pending", "deleted" }) await CreateUserAsync(app, name);
        var browser = NewBrowser(app);
        var locked = (await SignedInAsync(browser, "locked")).Model!["accessToken"]!.GetValue<string>();
        var pending = (await SignedInAsync(browser, "pending")).Model!["accessToken"]!.GetValue<string>();
        var deleted = (await SignedInAsync(browser, "deleted")).Model!["accessToken"]!.GetValue<string>();

        await UpdateUserAsync(app, "locked", (_, user) => user.LockoutEnd = DateTimeOffset.UtcNow.AddHours(1));
        await UpdateUserAsync(app, "pending", (_, user) => user.PendingApproval = true);
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
            await users.DeleteAsync((await users.FindByNameAsync("deleted"))!);
        }

        foreach (var token in new[] { pending, deleted })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings", bearer: token)).Status);
        }

        // Guessing someone's password locks the account for sign-ins; it does not end the sessions it has (a cookie session survives too).
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings", bearer: locked)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SignInAsync(browser, "locked")).Status);
    }

    // ----- where a session token is accepted

    [Fact]
    public async Task ASessionTokenIsNotACredentialOnProtocolRoutesTheAdministrationApiOrTheRegistry()
    {
        var app = Start();
        await CreateUserAsync(app, "root", administrator: true);
        var browser = NewBrowser(app);
        var access = (await SignedInAsync(browser, "root")).Model!["accessToken"]!.GetValue<string>();

        // Works on /api/ui for an administrator ...
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(browser, HttpMethod.Get, "/api/ui/admin/users", bearer: access)).Status);

        // ... but not as a key anywhere else.
        using var package = TestPackages.Build("Session.Lib", "1.0.0");
        using var push = TestPackages.PushRequest(package, access);
        push.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        using var pushed = await browser.SendAsync(push);
        Assert.Equal(HttpStatusCode.Unauthorized, pushed.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Get, "/api/v1/repositories", bearer: access)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Post, "/v2/default/image/blobs/uploads/", bearer: access)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Get, "/api/v1/repositories", key: access)).Status);
    }

    // ----- interactive-only twins

    [Fact]
    public async Task AccountTwinsAnswerASessionButNeverAKeyAnAnonymousCallerOrABareCookie()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        var browser = NewBrowser(app);
        var access = (await SignedInAsync(browser, "alice")).Model!["accessToken"]!.GetValue<string>();
        string apiKey;
        using (var scope = app.Services.CreateScope())
        {
            apiKey = await scope.ServiceProvider.GetRequiredService<ApiKeyService>().CreateAsync((await FindAsync(app, "alice"))!.Id, "test", ApiKeyScopes.All, "*", null, null, Token);
        }

        var viaSession = await SendAsync(browser, HttpMethod.Get, "/api/ui/account/apikeys", bearer: access);
        var viaKey = await SendAsync(browser, HttpMethod.Get, "/api/ui/account/apikeys", bearer: apiKey);
        var viaHeaderKey = await SendAsync(browser, HttpMethod.Get, "/api/ui/account/apikeys", key: apiKey);
        var anonymous = await SendAsync(NewBrowser(app), HttpMethod.Get, "/api/ui/account/apikeys");

        Assert.Equal(HttpStatusCode.OK, viaSession.Status);
        Assert.Equal("Ok", viaSession.Json!["status"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Forbidden, viaKey.Status);
        Assert.Contains("web screen", viaKey.Error, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, viaHeaderKey.Status);
        Assert.Equal(HttpStatusCode.Forbidden, anonymous.Status);
    }

    [Fact]
    public async Task ASessionConfirmsASensitiveActionWithThePasswordInTheBody()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        var browser = NewBrowser(app);
        var access = (await SignedInAsync(browser, "alice")).Model!["accessToken"]!.GetValue<string>();

        var wrong = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/twofactor/reset", new { password = "nope" }, bearer: access);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.Status);

        // A refusal changes nothing: no key was generated, so no renewal either.
        Assert.False(wrong.Headers.Contains(SessionHeaders.AccessToken));
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings", bearer: access)).Status);

        var right = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/twofactor/reset", new { password = Password }, bearer: access);
        Assert.Equal(HttpStatusCode.OK, right.Status);

        // Resetting the authenticator changes the security stamp: the session that did it got a renewed pair in the headers, the old tokens are void.
        var renewed = right.Headers.GetValues(SessionHeaders.AccessToken).Single();
        Assert.StartsWith(SessionTokenService.Prefix, renewed, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings", bearer: access)).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/settings", bearer: renewed)).Status);
        var refresh = right.Headers.GetValues(SessionHeaders.RefreshToken).Single();
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session/refresh", new { refreshToken = refresh })).Status);
    }

    [Fact]
    public async Task TheSetupKeyIsOnlyHandedToASessionThatConfirmedItsPasswordAndEnablingNeedsThePasswordToo()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        var browser = NewBrowser(app);
        var access = (await SignedInAsync(browser, "alice")).Model!["accessToken"]!.GetValue<string>();

        var plain = await SendAsync(browser, HttpMethod.Get, "/api/ui/account/twofactor", bearer: access);
        Assert.Equal(HttpStatusCode.OK, plain.Status);
        Assert.Null(plain.Model!["sharedKey"]);
        Assert.Null(plain.Model["qrCodeSvg"]);
        Assert.False(plain.Headers.Contains(SessionHeaders.AccessToken));

        var wrong = await SendAsync(browser, HttpMethod.Get, "/api/ui/account/twofactor", bearer: access, confirm: "nope");
        Assert.Null(wrong.Model!["sharedKey"]);

        var confirmed = await SendAsync(browser, HttpMethod.Get, "/api/ui/account/twofactor", bearer: access, confirm: Password);
        var key = confirmed.Model!["sharedKey"]!.GetValue<string>().Replace(" ", string.Empty);
        Assert.NotNull(confirmed.Model["qrCodeSvg"]);
        var renewed = confirmed.Headers.GetValues(SessionHeaders.AccessToken).Single();

        // The code alone is not enough, nor a wrong password.
        var noPassword = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/twofactor/enable", new { code = Totp(key) }, bearer: renewed);
        var badPassword = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/twofactor/enable", new { code = Totp(key), password = "nope" }, bearer: renewed);
        Assert.Equal(HttpStatusCode.BadRequest, noPassword.Status);
        Assert.Equal(HttpStatusCode.BadRequest, badPassword.Status);
        Assert.False((await FindAsync(app, "alice"))!.TwoFactorEnabled);

        var enabled = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/twofactor/enable", new { code = Totp(key), password = Password }, bearer: renewed);
        Assert.Equal(HttpStatusCode.OK, enabled.Status);
        Assert.True((await FindAsync(app, "alice"))!.TwoFactorEnabled);
        Assert.NotEmpty(enabled.Model!["recoveryCodes"]!.AsArray());
    }

    [Fact]
    public async Task AnApiKeyCannotBeMintedBySessionWithoutThePassword()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        var browser = NewBrowser(app);
        var access = (await SignedInAsync(browser, "alice")).Model!["accessToken"]!.GetValue<string>();
        var form = new { description = "ci", scopes = new[] { "package:push" }, packageGlobPattern = "*", expiresInDays = 30 };
        var withPassword = new { description = "ci", scopes = new[] { "package:push" }, packageGlobPattern = "*", expiresInDays = 30, password = Password };

        var without = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/apikeys/create", form, bearer: access);
        var wrong = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/apikeys/create", new { description = "ci", scopes = new[] { "package:push" }, packageGlobPattern = "*", expiresInDays = 30, password = "nope" }, bearer: access);
        var with = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/apikeys/create", withPassword, bearer: access);

        Assert.Equal(HttpStatusCode.BadRequest, without.Status);
        Assert.NotNull(without.Json!["errors"]!["Password"]);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.Status);
        Assert.Equal(HttpStatusCode.OK, with.Status);
        Assert.NotNull(with.Model!["createdKey"]);
    }

    [Fact]
    public async Task AnAccountWithoutAPasswordConfirmsOnlyWithinTheWindowOfItsAuthenticationTime()
    {
        var app = Start();
        string userId;
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
            var user = new BaGetUser { UserName = "external", Created = DateTime.UtcNow };
            Assert.True((await users.CreateAsync(user)).Succeeded);
            userId = user.Id;
        }

        string Token(DateTime authenticatedAt)
        {
            using var scope = app.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
            var user = users.FindByIdAsync(userId).GetAwaiter().GetResult()!;

            return scope.ServiceProvider.GetRequiredService<SessionTokenService>()
                .Issue(SessionTokenKind.Access, userId, users.GetSecurityStampAsync(user).GetAwaiter().GetResult()!, authenticatedAt).Token;
        }

        var browser = NewBrowser(app);
        var fresh = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/twofactor/reset", new { }, bearer: Token(_clock.Now.AddMinutes(-1)));
        var stale = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/twofactor/reset", new { }, bearer: Token(_clock.Now.AddMinutes(-10)));

        Assert.Equal(HttpStatusCode.OK, fresh.Status);
        Assert.Equal(HttpStatusCode.BadRequest, stale.Status);
    }

    [Fact]
    public async Task AdministrationTwinsAdmitAnAdministratorsSessionButNotAnotherUsersNorTheGlobalKey()
    {
        var app = Start();
        await CreateUserAsync(app, "root", administrator: true);
        await CreateUserAsync(app, "alice");
        var browser = NewBrowser(app);
        var root = (await SignedInAsync(browser, "root")).Model!["accessToken"]!.GetValue<string>();
        var alice = (await SignedInAsync(browser, "alice")).Model!["accessToken"]!.GetValue<string>();
        string adminKey;
        using (var scope = app.Services.CreateScope())
        {
            adminKey = await scope.ServiceProvider.GetRequiredService<ApiKeyService>().CreateAsync((await FindAsync(app, "root"))!.Id, "admin", new[] { ApiKeyScopes.Admin }, "*", null, null, Token);
        }

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(browser, HttpMethod.Get, "/api/ui/admin/users", bearer: root)).Status);
        var nonAdministrator = await SendAsync(browser, HttpMethod.Get, "/api/ui/admin/users", bearer: alice);
        Assert.Equal(HttpStatusCode.Forbidden, nonAdministrator.Status);
        Assert.Contains("administrator", nonAdministrator.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(browser, HttpMethod.Get, "/api/ui/admin/users", bearer: GlobalKey)).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(browser, HttpMethod.Get, "/api/ui/admin/users", bearer: adminKey)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(NewBrowser(app), HttpMethod.Get, "/api/ui/admin/users")).Status);
    }

    // ----- the cookie with an antiforgery header

    [Fact]
    public async Task TheCookieIsAcceptedOnlyWithAValidAntiforgeryHeaderForEveryMethod()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        var browser = NewBrowser(app);
        await AdminTestSupport.SignInAsync(browser, "alice");

        var tokenAnswer = await SendAsync(browser, HttpMethod.Get, "/api/ui/antiforgery");
        Assert.Equal(HttpStatusCode.OK, tokenAnswer.Status);
        Assert.Equal("X-XSRF-TOKEN", tokenAnswer.Model!["headerName"]!.GetValue<string>());
        var xsrf = tokenAnswer.Model["requestToken"]!.GetValue<string>();

        // The bare cookie, a wrong token and another browser's token are anonymous (403 on an interactive-only twin).
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/apikeys")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/apikeys", xsrf: "not-a-token")).Status);
        var stranger = NewBrowser(app);
        var strangerToken = (await SendAsync(stranger, HttpMethod.Get, "/api/ui/antiforgery")).Model!["requestToken"]!.GetValue<string>();
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/apikeys", xsrf: strangerToken)).Status);

        // With the token, GET and POST work.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/apikeys", xsrf: xsrf)).Status);
        var post = await SendAsync(browser, HttpMethod.Post, "/api/ui/account/twofactor/reset", new { password = Password }, xsrf: xsrf);
        Assert.Equal(HttpStatusCode.OK, post.Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(browser, HttpMethod.Post, "/api/ui/account/twofactor/reset", new { password = Password })).Status);

        // The token is the signed-in user's: it means nothing without the cookie.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(stranger, HttpMethod.Get, "/api/ui/account/apikeys", xsrf: xsrf)).Status);
    }

    [Fact]
    public async Task TheCookieWithTheHeaderOpensAnAdministratorsScreensAndTheHeaderAloneNothing()
    {
        var app = Start();
        await CreateUserAsync(app, "root", administrator: true);
        await CreateUserAsync(app, "alice");
        var root = NewBrowser(app);
        var alice = NewBrowser(app);
        await AdminTestSupport.SignInAsync(root, "root");
        await AdminTestSupport.SignInAsync(alice, "alice");
        var rootToken = (await SendAsync(root, HttpMethod.Get, "/api/ui/antiforgery")).Model!["requestToken"]!.GetValue<string>();
        var aliceToken = (await SendAsync(alice, HttpMethod.Get, "/api/ui/antiforgery")).Model!["requestToken"]!.GetValue<string>();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(root, HttpMethod.Get, "/api/ui/admin/users")).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(root, HttpMethod.Get, "/api/ui/admin/users", xsrf: rootToken)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(alice, HttpMethod.Get, "/api/ui/admin/users", xsrf: aliceToken)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(NewBrowser(app), HttpMethod.Get, "/api/ui/admin/users", xsrf: rootToken)).Status);
    }

    [Fact]
    public async Task AKeyOrATokenWinsOverTheCookieAndTheCookieDoesNotReauthenticateForAKey()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        await CreateUserAsync(app, "bob");
        var browser = NewBrowser(app);
        await AdminTestSupport.SignInAsync(browser, "alice");
        var xsrf = (await SendAsync(browser, HttpMethod.Get, "/api/ui/antiforgery")).Model!["requestToken"]!.GetValue<string>();
        string bobKey;
        using (var scope = app.Services.CreateScope())
        {
            bobKey = await scope.ServiceProvider.GetRequiredService<ApiKeyService>().CreateAsync((await FindAsync(app, "bob"))!.Id, "test", ApiKeyScopes.All, "*", null, null, Token);
        }

        // A key sent beside a valid cookie and header is judged as a key: the interactive-only twin refuses it.
        var answer = await SendAsync(browser, HttpMethod.Get, "/api/ui/account/apikeys", bearer: bobKey, xsrf: xsrf);

        Assert.Equal(HttpStatusCode.Forbidden, answer.Status);
    }

    [Fact]
    public async Task TheAntiforgeryEndpointSetsItsCookieAndIsNotCached()
    {
        var app = Start();
        var browser = NewBrowser(app);

        var answer = await SendAsync(browser, HttpMethod.Get, "/api/ui/antiforgery");

        Assert.Equal(HttpStatusCode.OK, answer.Status);
        Assert.True(answer.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.Contains("Antiforgery", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("no-cache", answer.Headers.CacheControl!.ToString() + string.Join(",", answer.Headers.Pragma.Select(p => p.Name)), StringComparison.OrdinalIgnoreCase);
    }

    // ----- the external provider

    [Fact]
    public async Task TheExternalSignInReturnsTokensInTheFragmentOfAnAllowedAddressAndLeavesNoCookie()
    {
        var app = new BaGetApplication(
            _output,
            configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "true" }, { "ApiKey", "" } },
            configureServices: services => services.AddTransient<IStartupFilter, ExternalCookieFilter>().AddSingleton<SystemTime>(_clock));
        _disposables.Add(app);
        var browser = NewBrowser(app);

        using (var provider = await browser.GetAsync("test/external?sub=s1&email=alice%40example.com&name=alice&verified=true")) Assert.Equal(HttpStatusCode.NoContent, provider.StatusCode);
        using var callback = await browser.GetAsync("api/ui/session/external-callback?returnUrl=" + Uri.EscapeDataString("/app/"));

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        var location = callback.Headers.Location!.ToString();
        Assert.StartsWith("/app/#exchange_token=" + Uri.EscapeDataString(SessionTokenService.Prefix), location, StringComparison.Ordinal);
        Assert.Contains("&user_name=alice", location, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", location, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh_token", location, StringComparison.Ordinal);
        Assert.DoesNotContain("?", location.Substring(0, location.IndexOf('#')), StringComparison.Ordinal);
        Assert.Contains("no-store", callback.Headers.CacheControl!.ToString(), StringComparison.Ordinal);

        // The page's cookie was not kept.
        Assert.DoesNotContain("account/settings", await browser.GetPageAsync(""), StringComparison.Ordinal);

        // The exchange token is turned into the session within a minute, and is worth nothing as a credential.
        var query = System.Web.HttpUtility.ParseQueryString(location.Substring(location.IndexOf('#') + 1));
        var exchange = query["exchange_token"]!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Get, "/api/ui/account/apikeys", bearer: exchange)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session/refresh", new { refreshToken = exchange })).Status);

        var exchanged = await SendAsync(browser, HttpMethod.Post, "/api/ui/session/exchange", new { exchangeToken = exchange });
        Assert.Equal(HttpStatusCode.OK, exchanged.Status);

        // Each exchange token works once (it travels in a URL fragment).
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session/exchange", new { exchangeToken = exchange })).Status);
        var answer = await SendAsync(browser, HttpMethod.Get, "/api/ui/account/apikeys", bearer: exchanged.Model!["accessToken"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, answer.Status);

        // A session token is not an exchange token, and the exchange token expires after a minute.
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session/exchange", new { exchangeToken = exchanged.Model["accessToken"]!.GetValue<string>() })).Status);
        _clock.Now = _clock.Now.AddSeconds(61);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(browser, HttpMethod.Post, "/api/ui/session/exchange", new { exchangeToken = exchange })).Status);
    }

    [Fact]
    public async Task TheExternalSignInOfAnAccountWithTwoFactorReturnsATicketAndAnUnusableOneAnError()
    {
        var app = new BaGetApplication(
            _output,
            configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "true" }, { "ApiKey", "" } },
            configureServices: services => services.AddTransient<IStartupFilter, ExternalCookieFilter>().AddSingleton<SystemTime>(_clock));
        _disposables.Add(app);
        var browser = NewBrowser(app);
        using (await browser.GetAsync("test/external?sub=s1&email=alice%40example.com&name=alice&verified=true")) { }
        using (await browser.GetAsync("api/ui/session/external-callback?returnUrl=%2Fapp%2F")) { }
        await EnableTwoFactorAsync(app, "alice");

        using (await browser.GetAsync("test/external?sub=s1&email=alice%40example.com&name=alice&verified=true")) { }
        using var second = await browser.GetAsync("api/ui/session/external-callback?returnUrl=%2Fapp%2F");
        var exchangeToken = System.Web.HttpUtility.ParseQueryString(second.Headers.Location!.ToString().Split('#')[1])["exchange_token"]!;
        Assert.DoesNotContain("access_token", second.Headers.Location.ToString(), StringComparison.Ordinal);
        var ticket = await SendAsync(browser, HttpMethod.Post, "/api/ui/session/exchange", new { exchangeToken });
        Assert.True(ticket.Model!["twoFactorRequired"]!.GetValue<bool>());
        Assert.Null(ticket.Model["accessToken"]);

        await UpdateUserAsync(app, "alice", (_, user) => user.PendingApproval = true);
        using (await browser.GetAsync("test/external?sub=s1&email=alice%40example.com&name=alice&verified=true")) { }
        using var pending = await browser.GetAsync("api/ui/session/external-callback?returnUrl=%2Fapp%2F");
        Assert.Contains("#error=", pending.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", pending.Headers.Location.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://evil.example/app/")]
    [InlineData("//evil.example/app/")]
    [InlineData("/\\evil.example/app/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/app/#x")]
    [InlineData("")]
    public async Task TheExternalSignInRefusesAReturnAddressOutsideThisServerAndTheListedOrigins(string returnUrl)
    {
        var app = Start(new Dictionary<string, string> { { "Cors:AllowedOrigins", "https://spa.example.com" } });
        var browser = NewBrowser(app);

        var start = await SendAsync(browser, HttpMethod.Get, "/api/ui/session/external/oidc?returnUrl=" + Uri.EscapeDataString(returnUrl));
        var callback = await SendAsync(browser, HttpMethod.Get, "/api/ui/session/external-callback?returnUrl=" + Uri.EscapeDataString(returnUrl));

        Assert.Equal(HttpStatusCode.BadRequest, start.Status);
        Assert.Equal(HttpStatusCode.BadRequest, callback.Status);
    }

    [Fact]
    public async Task TheExternalSignInIs404WithoutAProviderOrForAnotherProviderName()
    {
        var app = Start();
        var browser = NewBrowser(app);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(browser, HttpMethod.Get, "/api/ui/session/external/oidc?returnUrl=%2Fapp%2F")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(browser, HttpMethod.Get, "/api/ui/session/external/other?returnUrl=%2Fapp%2F")).Status);
    }

    [Theory]
    [InlineData("/app/", true)]
    [InlineData("/", true)]
    [InlineData("https://baget.example.com/app/", true)]
    [InlineData("https://BAGET.example.com/x", true)]
    [InlineData("https://spa.example.com/callback", true)]
    [InlineData("https://spa.example.com:8443/callback", false)]
    [InlineData("http://baget.example.com/app/", false)]
    [InlineData("https://baget.example.com.evil.example/", false)]
    [InlineData("https://user@spa.example.com/", false)]
    [InlineData("https://spa.example.com\\@evil.example/", false)]
    [InlineData("ftp://spa.example.com/", false)]
    [InlineData("app/", false)]
    [InlineData(null, false)]
    public void ReturnAddressesAreJudgedStrictly(string? returnUrl, bool allowed)
    {
        var origins = SessionReturnUrls.Origins("https://spa.example.com/; ");

        Assert.Equal(allowed, SessionReturnUrls.IsAllowed(returnUrl, "https://baget.example.com", origins));
    }
}
