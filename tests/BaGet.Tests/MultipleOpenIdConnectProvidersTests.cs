using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Services.Accounts;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Xunit;
using static BaGet.Tests.AdminTestSupport;

namespace BaGet.Tests;

/// <summary>
/// Several OpenID Connect providers (<c>Accounts:OpenIdConnectProviders:&lt;id&gt;</c>) beside the single <c>Accounts:OpenIdConnect</c> block (provider <c>oidc</c>):
/// registration, the sign-in page, the challenge and callback per provider, separate accounts, linking, re-authentication, claim names and the
/// administrator/group mapping. The provider's answer is stood in for by <see cref="ExternalCookieFilter"/>.
/// </summary>
public class MultipleOpenIdConnectProvidersTests : IDisposable
{
    private const string P = "Accounts:OpenIdConnectProviders:";

    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public MultipleOpenIdConnectProvidersTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }

    private static Dictionary<string, string> Providers(params string[] ids)
    {
        var settings = new Dictionary<string, string> { { "Accounts:Enabled", "true" }, { "ApiKey", "" } };
        foreach (var id in ids)
        {
            settings[P + id + ":Authority"] = $"https://{id}.example.com";
            settings[P + id + ":ClientId"] = "client-" + id;
            settings[P + id + ":ClientSecret"] = "secret-" + id;
            settings[P + id + ":DisplayName"] = char.ToUpperInvariant(id[0]) + id[1..];
        }

        return settings;
    }

    private (BaGetApplication App, Browser Browser) Start(Dictionary<string, string> settings, RecordingAudit? audit = null)
    {
        var app = new BaGetApplication(
            _output,
            configOverrides: settings,
            configureServices: services =>
            {
                services.AddTransient<IStartupFilter, ExternalCookieFilter>();
                if (audit != null) services.AddSingleton<IAdminAudit>(audit);

                // The metadata a real provider would serve (no network): only the authorization endpoint matters for a challenge.
                foreach (var scheme in settings.Keys.Where(k => k.StartsWith(P, StringComparison.Ordinal) && k.EndsWith(":Authority", StringComparison.Ordinal))
                             .Select(k => k[P.Length..^":Authority".Length])
                             .Append("oidc"))
                {
                    services.PostConfigure<OpenIdConnectOptions>(scheme, options =>
                        options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(new OpenIdConnectConfiguration
                        {
                            Issuer = options.Authority,
                            AuthorizationEndpoint = options.Authority + "/authorize",
                        }));
                }
            });
        var browser = new Browser(app);
        _disposables.Add(app);
        _disposables.Add(browser);

        return (app, browser);
    }

    internal sealed class RecordingAudit : IAdminAudit
    {
        public List<(string? Actor, string Action, string Subject, string? Details)> Entries { get; } = new();

        public void Record(string? actor, string action, string subject, string? details = null)
        {
            lock (Entries) Entries.Add((actor, action, subject, details));
        }
    }

    private static async Task<HttpResponseMessage> AnswerAsync(Browser browser, string provider, string sub, string name, string? email = "alice@example.com", string? verified = "true", string? xsrf = null, string? extra = null, long? authTime = null)
    {
        var url = $"test/external?provider={provider}&sub={Uri.EscapeDataString(sub)}&name={Uri.EscapeDataString(name)}";
        if (email != null) url += "&email=" + Uri.EscapeDataString(email);
        if (verified != null) url += "&verified=" + verified;
        if (xsrf != null) url += "&xsrf=" + Uri.EscapeDataString(xsrf);
        if (authTime != null) url += "&auth_time=" + authTime;
        if (extra != null) url += "&" + extra;
        var answer = await browser.GetAsync(url);
        Assert.Equal(HttpStatusCode.NoContent, answer.StatusCode);

        return answer;
    }

    private static async Task<HttpResponseMessage> SignInAsync(Browser browser, string provider, string sub, string name, string? email = "alice@example.com", string? verified = "true", string? extra = null)
    {
        using (await AnswerAsync(browser, provider, sub, name, email, verified, extra: extra)) { }

        return await browser.GetAsync("account/login?handler=Callback");
    }

    private static Task<BaGetUser?> UserAsync(BaGetApplication app, string name)
        => WithAsync(app, s => s.GetRequiredService<UserManager<BaGetUser>>().FindByNameAsync(name));

    private static Task<IList<string>> LoginProvidersAsync(BaGetApplication app, string name)
        => WithAsync(app, async s =>
        {
            var users = s.GetRequiredService<UserManager<BaGetUser>>();

            return (IList<string>)(await users.GetLoginsAsync((await users.FindByNameAsync(name))!)).Select(l => l.LoginProvider).ToList();
        });

    private static Task<List<string?>> UserNamesAsync(BaGetApplication app)
        => WithAsync(app, s => Task.FromResult(s.GetRequiredService<UserManager<BaGetUser>>().Users.Select(u => u.UserName).ToList()));

    private static async Task<bool> IsSignedInAsync(Browser browser) => (await browser.GetPageAsync("")).Contains("account/settings");

    private static Exception Flatten(Exception e)
    {
        while (e.InnerException != null) e = e.InnerException;

        return e;
    }

    // ----- registration

    [Fact]
    public void EachProviderGetsItsOwnSchemeCallbackPathScopesAndSecret()
    {
        var settings = Providers("google", "entra");
        settings[P + "entra:Scopes"] = "groups offline_access";
        var (app, _) = Start(settings);
        var options = app.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>();

        var google = options.Get("google");
        var entra = options.Get("entra");

        Assert.Equal("/signin-oidc-google", google.CallbackPath);
        Assert.Equal("/signin-oidc-entra", entra.CallbackPath);
        Assert.Equal("/signout-callback-oidc-entra", entra.SignedOutCallbackPath);
        Assert.Equal("https://google.example.com", google.Authority);
        Assert.Equal("client-entra", entra.ClientId);
        Assert.Equal("secret-entra", entra.ClientSecret);
        Assert.Equal(IdentityConstants.ExternalScheme, entra.SignInScheme);
        Assert.Contains("profile", google.Scope);
        Assert.Contains("email", google.Scope);
        Assert.DoesNotContain("groups", google.Scope);
        Assert.Contains("groups", entra.Scope);
        Assert.Contains("offline_access", entra.Scope);
        Assert.Equal("code", entra.ResponseType);
    }

    [Fact]
    public void TheSingleBlockStaysTheProviderOidcWithTheOriginalCallback()
    {
        var settings = Providers("entra");
        settings["Accounts:OpenIdConnect:Authority"] = "https://legacy.example.com";
        settings["Accounts:OpenIdConnect:ClientId"] = "legacy";
        settings["Accounts:OpenIdConnect:DisplayName"] = "Corporate";
        var (app, _) = Start(settings);
        var options = app.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>();
        var set = app.Services.GetRequiredService<OpenIdConnectProviderSet>();

        Assert.Equal("/signin-oidc", options.Get("oidc").CallbackPath);
        Assert.Equal("https://legacy.example.com", options.Get("oidc").Authority);
        Assert.Equal(new[] { "oidc", "entra" }, set.All.Select(p => p.Id));
        Assert.Equal("Corporate", set.Find("oidc")!.DisplayName);
    }

    [Fact]
    public void ANamedProviderCalledOidcBesideTheSingleBlockStopsTheStartup()
    {
        var settings = Providers("oidc");
        settings["Accounts:OpenIdConnect:Authority"] = "https://legacy.example.com";
        settings["Accounts:OpenIdConnect:ClientId"] = "legacy";

        var error = Flatten(Assert.ThrowsAny<Exception>(() => Start(settings)));

        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("Accounts:OpenIdConnect", error.Message);
    }

    [Theory]
    [InlineData("Has_Underscore")]
    [InlineData("with.dot")]
    [InlineData("0123456789012345678901234567890123")]
    public void AnInvalidProviderIdStopsTheStartup(string id)
    {
        var settings = Providers("x");
        settings[P + id + ":Authority"] = "https://bad.example.com";
        settings[P + id + ":ClientId"] = "bad";

        var error = Flatten(Assert.ThrowsAny<Exception>(() => Start(settings)));

        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("provider id", error.Message);
    }

    // ----- the sign-in page and the challenge

    [Fact]
    public async Task TheSignInPageOffersOneButtonPerProvider()
    {
        var (_, browser) = Start(Providers("google", "entra"));

        var html = await browser.GetPageAsync("account/login");

        Assert.Contains("name=\"provider\" value=\"entra\"", html);
        Assert.Contains("name=\"provider\" value=\"google\"", html);
        Assert.Contains("Sign in with Entra", html);
        Assert.Contains("Sign in with Google", html);
    }

    [Fact]
    public async Task TheSignInPageOffersNoProviderButtonWithoutAProvider()
    {
        var (_, browser) = Start(Providers());

        Assert.DoesNotContain("name=\"provider\"", await browser.GetPageAsync("account/login"));
    }

    [Fact]
    public async Task TheChallengeGoesToTheChosenProviderWithItsOwnCallback()
    {
        var (_, browser) = Start(Providers("google", "entra"));

        using var response = await browser.PostAsync("account/login", new Dictionary<string, string> { ["provider"] = "entra" }, action: "account/login?handler=External");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.StartsWith("https://entra.example.com/authorize", location);
        Assert.Contains("client_id=client-entra", location);
        Assert.Contains(Uri.EscapeDataString("/signin-oidc-entra"), location);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("")]
    public async Task AnUnknownProviderOrNoneAmongSeveralIs404OnTheSignInPage(string provider)
    {
        var (_, browser) = Start(Providers("google", "entra"));

        using var response = await browser.PostAsync("account/login", new Dictionary<string, string> { ["provider"] = provider }, action: "account/login?handler=External");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task APostWithoutAProviderMeansTheOnlyOneConfigured()
    {
        var (_, browser) = Start(Providers("google"));

        using var response = await browser.PostAsync("account/login", new Dictionary<string, string>(), action: "account/login?handler=External");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://google.example.com/authorize", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task TheSessionApiChallengesTheNamedProviderAndAnswers404ForAnUnknownOne()
    {
        var settings = Providers("google", "entra");
        settings["Cors:AllowedOrigins"] = "https://spa.example.com";
        var (_, browser) = Start(settings);

        using var unknown = await browser.GetAsync("api/ui/session/external/nope?returnUrl=" + Uri.EscapeDataString("https://spa.example.com/cb"));
        using var known = await browser.GetAsync("api/ui/session/external/Entra?returnUrl=" + Uri.EscapeDataString("https://spa.example.com/cb"));

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, known.StatusCode);
        var location = known.Headers.Location!.ToString();
        Assert.StartsWith("https://entra.example.com/authorize", location);
        Assert.Contains(Uri.EscapeDataString("/signin-oidc-entra"), location);
    }

    [Fact]
    public async Task TheSessionApiJudgesTheReturnAddressBeforeTheProviderAndAnIdThatCannotBeOneIs404()
    {
        var (_, browser) = Start(Providers("google"));

        using var bad = await browser.GetAsync("api/ui/session/external/google?returnUrl=" + Uri.EscapeDataString("https://evil.example/app/"));
        using var unknownBad = await browser.GetAsync("api/ui/session/external/entra?returnUrl=" + Uri.EscapeDataString("https://evil.example/app/"));
        using var unknownGood = await browser.GetAsync("api/ui/session/external/entra?returnUrl=" + Uri.EscapeDataString("/app/"));
        using var notAnId = await browser.GetAsync("api/ui/session/external/Not_An.Id?returnUrl=" + Uri.EscapeDataString("/app/"));

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownBad.StatusCode); // same answer: a bad address reveals nothing about the provider
        Assert.Equal(HttpStatusCode.NotFound, unknownGood.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notAnId.StatusCode);
    }

    [Fact]
    public async Task TheSessionApiExchangeFlowWorksForANamedProvider()
    {
        var (app, browser) = Start(Providers("google", "entra"));
        using (await AnswerAsync(browser, "entra", "e-1", "erin", "erin@example.com")) { }

        using var callback = await browser.GetAsync("api/ui/session/external-callback?returnUrl=" + Uri.EscapeDataString("/app/"));

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Contains("exchange_token=", callback.Headers.Location!.ToString());
        Assert.Contains("user_name=erin", callback.Headers.Location!.ToString());
        Assert.Equal(new[] { "entra" }, await LoginProvidersAsync(app, "erin"));
    }

    [Fact]
    public async Task ExtraRequestParametersAreSentButNeverReplaceTheOnesBaGetSets()
    {
        var settings = Providers("hint");
        settings[P + "hint:Parameters:domain_hint"] = "example.com";
        settings[P + "hint:Parameters:prompt"] = "none";
        settings[P + "hint:Parameters:state"] = "evil";
        settings[P + "hint:Parameters:redirect_uri"] = "https://evil.example/cb";
        var (_, browser) = Start(settings);

        using var response = await browser.PostAsync("account/login", new Dictionary<string, string> { ["provider"] = "hint" }, action: "account/login?handler=External");

        var location = response.Headers.Location!.ToString();
        Assert.Contains("domain_hint=example.com", location);
        Assert.DoesNotContain("prompt=none", location);
        Assert.DoesNotContain("state=evil", location);
        Assert.DoesNotContain("evil.example", location);
        Assert.Contains(Uri.EscapeDataString("/signin-oidc-hint"), location);
    }

    [Fact]
    public async Task ExtraParametersNeverWeakenAReauthentication()
    {
        var settings = Providers("hint");
        settings[P + "hint:Parameters:prompt"] = "none";
        settings[P + "hint:Parameters:max_age"] = "99999";
        settings[P + "hint:Parameters:domain_hint"] = "example.com";
        var (app, browser) = Start(settings);
        using (await SignInAsync(browser, "hint", "h-1", "alice")) { }

        using var response = await StartReauthenticationAsync(browser, null);

        var location = response.Headers.Location!.ToString();
        Assert.Contains("prompt=login", location);
        Assert.Contains("max_age=0", location);
        Assert.DoesNotContain("99999", location);
        Assert.Contains("domain_hint=example.com", location);
    }

    // ----- accounts

    [Fact]
    public async Task TheSamePersonAtTwoProvidersGetsTwoSeparateAccounts()
    {
        var (app, browser) = Start(Providers("google", "entra"));

        using (var first = await SignInAsync(browser, "google", "g-1", "alice")) Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        using (await browser.PostAsync("account/logout", new Dictionary<string, string>())) { }
        using (var second = await SignInAsync(browser, "entra", "e-1", "alice")) Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);

        Assert.Equal(new[] { "alice", "alice2" }, (await UserNamesAsync(app)).Where(n => n!.StartsWith("alice")).OrderBy(n => n));
        Assert.Equal(new[] { "google" }, await LoginProvidersAsync(app, "alice"));
        Assert.Equal(new[] { "entra" }, await LoginProvidersAsync(app, "alice2"));
    }

    [Fact]
    public async Task TheSameSubjectAtTwoProvidersIsTwoDifferentLogins()
    {
        var (app, browser) = Start(Providers("google", "entra"));
        using (await SignInAsync(browser, "google", "same", "gina", "gina@example.com")) { }
        using (await browser.PostAsync("account/logout", new Dictionary<string, string>())) { }

        using (await SignInAsync(browser, "entra", "same", "erin", "erin@example.com")) { }

        Assert.NotNull(await UserAsync(app, "gina"));
        Assert.NotNull(await UserAsync(app, "erin"));
        Assert.Equal(new[] { "google" }, await LoginProvidersAsync(app, "gina"));
    }

    [Fact]
    public async Task ASecondSignInThroughTheSameProviderFindsTheSameAccount()
    {
        var (app, browser) = Start(Providers("google", "entra"));
        using (await SignInAsync(browser, "google", "g-1", "alice")) { }
        using (await browser.PostAsync("account/logout", new Dictionary<string, string>())) { }

        using (var again = await SignInAsync(browser, "google", "g-1", "alice")) Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);

        Assert.Equal(1, (await UserNamesAsync(app)).Count(n => n!.StartsWith("alice")));
        Assert.True(await IsSignedInAsync(browser));
    }

    // ----- claim names

    [Fact]
    public async Task TheClaimNamesOfAProviderCanBeOverridden()
    {
        var settings = Providers("odd");
        settings[P + "odd:UserNameClaim"] = "login";
        settings[P + "odd:EmailClaim"] = "mail";
        settings[P + "odd:EmailVerifiedClaim"] = "mail_ok";
        var (app, browser) = Start(settings);

        using (var created = await SignInAsync(browser, "odd", "o-1", "", email: null, verified: null, extra: "c=login=bob.smith&c=mail=bob@corp.example&c=mail_ok=true"))
        {
            Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        }

        var bob = await UserAsync(app, "bob.smith");
        Assert.NotNull(bob);
        Assert.Equal("bob@corp.example", bob!.Email);
        Assert.True(bob.EmailConfirmed);
    }

    [Fact]
    public async Task TheStandardClaimNamesAreIgnoredWhenAProviderNamesOthers()
    {
        var settings = Providers("odd");
        settings[P + "odd:EmailVerifiedClaim"] = "mail_ok";
        var (app, browser) = Start(settings);

        // email_verified=true is the standard claim; this provider says mail_ok, which is absent.
        using (await SignInAsync(browser, "odd", "o-1", "carl", "carl@example.com", verified: "true")) { }

        var carl = await UserAsync(app, "carl");
        Assert.NotNull(carl);
        Assert.False(carl!.EmailConfirmed);
    }

    [Fact]
    public async Task TheEmailIsConfirmedOnlyWhenThatProvidersVerifiedClaimIsTrue()
    {
        var (app, browser) = Start(Providers("google", "entra"));

        using (await SignInAsync(browser, "google", "g-1", "gina", "gina@example.com", verified: "true")) { }
        using (await browser.PostAsync("account/logout", new Dictionary<string, string>())) { }
        using (await SignInAsync(browser, "entra", "e-1", "erin", "erin@example.com", verified: "false")) { }

        Assert.True((await UserAsync(app, "gina"))!.EmailConfirmed);
        Assert.False((await UserAsync(app, "erin"))!.EmailConfirmed);
    }

    // ----- linking and unlinking

    private async Task<(BaGetApplication App, Browser Browser, string UserId)> SignedInWithGoogleAsync(Dictionary<string, string>? settings = null)
    {
        var (app, browser) = Start(settings ?? Providers("google", "entra"));
        using (await SignInAsync(browser, "google", "g-1", "alice")) { }

        return (app, browser, (await UserAsync(app, "alice"))!.Id);
    }

    [Fact]
    public async Task AUserLinksASecondProviderAndTheSettingsPageListsBoth()
    {
        var (app, browser, id) = await SignedInWithGoogleAsync();
        using (await AnswerAsync(browser, "entra", "e-1", "alice", xsrf: id)) { }

        using (var linked = await browser.GetAsync("account/settings?handler=LinkCallback"))
        {
            Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
            Assert.Contains("Entra is linked to your account", await linked.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        Assert.Equal(new[] { "entra", "google" }, (await LoginProvidersAsync(app, "alice")).OrderBy(x => x));
        var page = await browser.GetPageAsync("account/settings");
        Assert.Contains("data-provider=\"google\"", page);
        Assert.Contains("data-provider=\"entra\"", page);
        Assert.Contains("Linked", page);
        Assert.Single(await UserNamesAsync(app), n => n == "alice"); // linking makes no second account
    }

    [Fact]
    public async Task ALoginAnotherAccountOwnsCannotBeLinked()
    {
        var (app, browser, id) = await SignedInWithGoogleAsync();
        using (var other = new Browser(app))
        {
            using (await SignInAsync(other, "entra", "e-9", "mallory", "mallory@example.com")) { }
        }

        using (await AnswerAsync(browser, "entra", "e-9", "alice", xsrf: id)) { }
        using var response = await browser.GetAsync("account/settings?handler=LinkCallback");

        Assert.Contains("already linked to another account", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(new[] { "google" }, await LoginProvidersAsync(app, "alice"));
    }

    [Fact]
    public async Task AnAnswerStartedByAnotherSessionIsNotLinked()
    {
        var (app, browser, _) = await SignedInWithGoogleAsync();
        using (await AnswerAsync(browser, "entra", "e-1", "alice", xsrf: "someone-elses-id")) { }

        using var response = await browser.GetAsync("account/settings?handler=LinkCallback");

        Assert.Contains("failed", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(new[] { "google" }, await LoginProvidersAsync(app, "alice"));
    }

    [Fact]
    public async Task StartingALinkAsksThePasswordAndNamesAKnownProvider()
    {
        var (app, browser) = Start(Providers("google", "entra"));
        await CreateUserAsync(app, "dave");
        await AdminTestSupport.SignInAsync(browser, "dave");

        using var wrong = await browser.PostAsync("account/settings", new Dictionary<string, string> { ["provider"] = "entra", ["password"] = "nope" }, action: "account/settings?handler=LinkExternal");
        using var unknown = await browser.PostAsync("account/settings", new Dictionary<string, string> { ["provider"] = "nope", ["password"] = Password }, action: "account/settings?handler=LinkExternal");
        using var right = await browser.PostAsync("account/settings", new Dictionary<string, string> { ["provider"] = "entra", ["password"] = Password }, action: "account/settings?handler=LinkExternal");

        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Contains("confirmation is incorrect", await wrong.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, right.StatusCode);
        Assert.StartsWith("https://entra.example.com/authorize", right.Headers.Location!.ToString());
    }

    [Fact]
    public async Task UnlinkingAsksThePasswordAndKeepsTheLastWayToSignIn()
    {
        var (app, browser) = Start(Providers("google", "entra"));
        await CreateUserAsync(app, "dave");
        await AdminTestSupport.SignInAsync(browser, "dave");
        var id = (await UserAsync(app, "dave"))!.Id;
        foreach (var (provider, sub) in new[] { ("google", "g-d"), ("entra", "e-d") })
        {
            using (await AnswerAsync(browser, provider, sub, "dave", xsrf: id)) { }
            using (await browser.GetAsync("account/settings?handler=LinkCallback")) { }
        }

        using (var wrong = await browser.PostAsync("account/settings", new Dictionary<string, string> { ["provider"] = "google", ["password"] = "nope" }, action: "account/settings?handler=UnlinkExternal"))
        {
            Assert.Contains("confirmation is incorrect", await wrong.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        Assert.Equal(2, (await LoginProvidersAsync(app, "dave")).Count);

        using (var right = await browser.PostAsync("account/settings", new Dictionary<string, string> { ["provider"] = "google", ["password"] = Password }, action: "account/settings?handler=UnlinkExternal"))
        {
            Assert.Contains("was unlinked", await right.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        Assert.Equal(new[] { "entra" }, await LoginProvidersAsync(app, "dave"));

        // A password exists, so the last login may go too; an account without one keeps its only login (next test).
        using (await browser.PostAsync("account/settings", new Dictionary<string, string> { ["provider"] = "entra", ["password"] = Password }, action: "account/settings?handler=UnlinkExternal")) { }
        Assert.Empty(await LoginProvidersAsync(app, "dave"));
    }

    [Fact]
    public async Task TheOnlyLoginOfAnAccountWithoutAPasswordCannotBeUnlinked()
    {
        var (app, browser, id) = await SignedInWithGoogleAsync();
        await ReauthenticateAsync(app, browser, "google", "g-1", id);

        using var response = await browser.PostAsync("account/settings", new Dictionary<string, string> { ["provider"] = "google", ["password"] = "" }, action: "account/settings?handler=UnlinkExternal");

        Assert.Contains("only way to sign in", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(new[] { "google" }, await LoginProvidersAsync(app, "alice"));
    }

    [Fact]
    public async Task LoginsAtProvidersNoLongerConfiguredAreNotAnotherWayInToUnlinkTheLastOne()
    {
        var (app, browser, id) = await SignedInWithGoogleAsync();
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
            await users.AddLoginAsync((await users.FindByNameAsync("alice"))!, new UserLoginInfo("retired", "r-1", "retired"));
        }

        await ReauthenticateAsync(app, browser, "google", "g-1", id);
        using var response = await browser.PostAsync("account/settings", new Dictionary<string, string> { ["provider"] = "google", ["password"] = "" }, action: "account/settings?handler=UnlinkExternal");

        Assert.Contains("only way to sign in", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("google", await LoginProvidersAsync(app, "alice"));
    }

    [Fact]
    public async Task APasswordlessUserWithTwoConfiguredLoginsSeesTheReauthenticateButtonOnLinkedRows()
    {
        var (_, browser, id) = await SignedInWithGoogleAsync();
        using (await AnswerAsync(browser, "entra", "e-1", "alice", xsrf: id)) { }
        using (await browser.GetAsync("account/settings?handler=LinkCallback")) { }

        var html = await browser.GetPageAsync("account/settings");
        var rows = html.Substring(html.IndexOf("external-logins", StringComparison.Ordinal));
        rows = rows.Substring(0, rows.IndexOf("Two-factor", StringComparison.Ordinal));

        Assert.Equal(2, rows.Split("formaction=\"/account/reauthenticate").Length - 1);
    }

    [Fact]
    public async Task TheSessionApiListsTheProvidersWithoutASession()
    {
        var (_, browser) = Start(Providers("google", "entra"));

        using var response = await browser.GetAsync("api/ui/session/providers");
        var json = System.Text.Json.Nodes.JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var providers = json["model"]!["providers"]!.AsArray();
        Assert.Equal(new[] { "entra", "google" }, providers.Select(p => p!["id"]!.GetValue<string>()));
        Assert.Equal("Entra", providers[0]!["displayName"]!.GetValue<string>());
        Assert.DoesNotContain("secret", json.ToJsonString());
    }

    [Fact]
    public async Task TheProvidersListIsEmptyWithoutProvidersAndRefusesAForeignBrowserOrigin()
    {
        var (_, browser) = Start(Providers());
        using var empty = await browser.GetAsync("api/ui/session/providers");
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Empty(System.Text.Json.Nodes.JsonNode.Parse(await empty.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["model"]!["providers"]!.AsArray());

        using var foreign = new HttpRequestMessage(HttpMethod.Get, "api/ui/session/providers");
        foreign.Headers.Add("Origin", "https://evil.example");
        using var refused = await browser.SendAsync(foreign);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    // ----- re-authentication

    private static async Task ReauthenticateAsync(BaGetApplication app, Browser browser, string provider, string sub, string userId)
    {
        var now = new DateTimeOffset(app.Services.GetRequiredService<SystemTime>().UtcNow).ToUnixTimeSeconds();
        using (await AnswerAsync(browser, provider, sub, "alice", xsrf: userId, authTime: now)) { }
        using var back = await browser.GetAsync("account/reauthenticate?handler=Callback");
        Assert.Equal(HttpStatusCode.Redirect, back.StatusCode);
    }

    private static Task<HttpResponseMessage> StartReauthenticationAsync(Browser browser, string? provider)
        => browser.PostAsync(
            "account/settings",
            provider == null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["provider"] = provider },
            action: "account/reauthenticate",
            tokenPage: "account/settings");

    [Fact]
    public async Task ReauthenticationWithTheOnlyLinkedProviderGoesStraightToIt()
    {
        var (_, browser, _) = await SignedInWithGoogleAsync();

        using var response = await StartReauthenticationAsync(browser, null);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.StartsWith("https://google.example.com/authorize", location);
        Assert.Contains("prompt=login", location);
        Assert.Contains("max_age=0", location);
    }

    [Fact]
    public async Task WithSeveralLinkedProvidersTheUserPicksOneAndOnlyALinkedOneIsAccepted()
    {
        var (_, browser, id) = await SignedInWithGoogleAsync(Providers("google", "entra", "other"));
        using (await AnswerAsync(browser, "entra", "e-1", "alice", xsrf: id)) { }
        using (await browser.GetAsync("account/settings?handler=LinkCallback")) { }

        using var choice = await StartReauthenticationAsync(browser, null);
        using var entra = await StartReauthenticationAsync(browser, "ENTRA");
        using var notLinked = await StartReauthenticationAsync(browser, "other");

        Assert.Equal(HttpStatusCode.OK, choice.StatusCode);
        var html = await choice.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("value=\"google\"", html);
        Assert.Contains("value=\"entra\"", html);
        Assert.DoesNotContain("value=\"other\"", html);
        Assert.Equal(HttpStatusCode.Redirect, entra.StatusCode);
        Assert.StartsWith("https://entra.example.com/authorize", entra.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, notLinked.StatusCode);
        Assert.Contains("not linked", await notLinked.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AFreshAnswerOfTheLinkedSecondProviderConfirmsTheAction()
    {
        var (app, browser, id) = await SignedInWithGoogleAsync();
        using (await AnswerAsync(browser, "entra", "e-1", "alice", xsrf: id)) { }
        using (await browser.GetAsync("account/settings?handler=LinkCallback")) { }

        await ReauthenticateAsync(app, browser, "entra", "e-1", id);

        Assert.Contains("signed in again", await browser.GetPageAsync("account/settings"));
    }

    [Fact]
    public async Task AFreshAnswerOfAnotherAccountAtTheSameProviderIsNotAccepted()
    {
        var (app, browser, id) = await SignedInWithGoogleAsync();
        var now = new DateTimeOffset(app.Services.GetRequiredService<SystemTime>().UtcNow).ToUnixTimeSeconds();
        using (await AnswerAsync(browser, "google", "someone-else", "alice", xsrf: id, authTime: now)) { }

        using var back = await browser.GetAsync("account/reauthenticate?handler=Callback");

        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        Assert.DoesNotContain("signed in again", await browser.GetPageAsync("account/settings"));
    }

    [Fact]
    public async Task AUserWithoutALinkedProviderCannotReauthenticateAndIsNotOfferedTo()
    {
        var (app, browser) = Start(Providers("google", "entra"));
        await CreateUserAsync(app, "dave");
        await AdminTestSupport.SignInAsync(browser, "dave");
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
        var logins = scope.ServiceProvider.GetRequiredService<ExternalLogins>();

        Assert.False(await logins.CanReauthenticateAsync(await users.FindByNameAsync("dave")));
        Assert.Equal(2, logins.Configured.Count);
        using var response = await StartReauthenticationAsync(browser, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no external provider linked", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReauthenticationIsOfferedToAUserWithALinkedProvider()
    {
        var (app, browser, _) = await SignedInWithGoogleAsync();

        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
        var logins = scope.ServiceProvider.GetRequiredService<ExternalLogins>();

        Assert.True(await logins.CanReauthenticateAsync(await users.FindByNameAsync("alice")));
        Assert.Contains("formaction=\"/account/reauthenticate", await browser.GetPageAsync("account/settings"));
    }

    // ----- role and group mapping

    private static Dictionary<string, string> Mapped(params string[] ids)
    {
        var settings = Providers(ids);
        settings[P + "corp:AdministratorClaim"] = "roles";
        settings[P + "corp:AdministratorValue"] = "baget-admin";
        settings[P + "corp:GroupClaim"] = "groups";
        settings[P + "corp:GroupMappings:dev"] = "Developers";
        settings[P + "corp:GroupMappings:ops"] = "Operators";
        settings[P + "corp:GroupMappings:qa"] = "Testers";

        return settings;
    }

    private static Task CreateGroupAsync(BaGetApplication app, string name)
        => WithAsync(app, async s =>
        {
            await s.GetRequiredService<IAccessAdministration>().CreateGroupAsync(name, null, "test", TestContext.Current.CancellationToken);

            return 0;
        });

    private static Task<IReadOnlyList<string>> GroupsOfAsync(BaGetApplication app, string name)
        => WithAsync(app, async s => await s.GetRequiredService<IAccessAdministration>()
            .GroupsOfUserAsync((await s.GetRequiredService<UserManager<BaGetUser>>().FindByNameAsync(name))!.Id, TestContext.Current.CancellationToken));

    [Fact]
    public async Task TheProviderPromotesAnAdministratorClaimHolderAndDemotesWhenItIsGone()
    {
        var audit = new RecordingAudit();
        var (app, browser) = Start(Mapped("corp"), audit);
        await CreateUserAsync(app, "boss", administrator: true); // another administrator, so alice may be demoted

        using (await SignInAsync(browser, "corp", "c-1", "alice", extra: "c=roles=baget-admin&c=roles=other")) { }
        Assert.True((await UserAsync(app, "alice"))!.IsAdministrator);
        Assert.Contains(audit.Entries, e => e.Actor == "provider:corp" && e.Action == "account.promote" && e.Subject.Contains("alice"));

        using (await browser.PostAsync("account/logout", new Dictionary<string, string>())) { }
        using (await SignInAsync(browser, "corp", "c-1", "alice", extra: "c=roles=other")) { }

        Assert.False((await UserAsync(app, "alice"))!.IsAdministrator);
        Assert.Contains(audit.Entries, e => e.Actor == "provider:corp" && e.Action == "account.demote" && e.Subject.Contains("alice"));
    }

    [Fact]
    public async Task TheLastAdministratorIsNeverDemotedByAProvider()
    {
        var settings = Mapped("corp");
        settings["Accounts:CreateDefaultAdministrator"] = "false";
        var (app, browser) = Start(settings);

        using (await SignInAsync(browser, "corp", "c-1", "alice", extra: "c=roles=baget-admin")) { }
        Assert.True((await UserAsync(app, "alice"))!.IsAdministrator);
        using (await browser.PostAsync("account/logout", new Dictionary<string, string>())) { }

        using (await SignInAsync(browser, "corp", "c-1", "alice", extra: "c=roles=none")) { }

        Assert.True((await UserAsync(app, "alice"))!.IsAdministrator);
        Assert.True(await IsSignedInAsync(browser));
    }

    [Fact]
    public async Task AProviderWithoutAMappingNeverChangesAdministratorsOrGroupsAndAnotherProvidersMappingOnlyTouchesItsOwnUsers()
    {
        var settings = Mapped("corp", "plain");
        var (app, browser) = Start(settings);
        await CreateGroupAsync(app, "Developers");

        // bob signs in through "plain" (no mapping): the claims mean nothing there.
        using (await SignInAsync(browser, "plain", "p-1", "bob", extra: "c=roles=baget-admin&c=groups=dev")) { }
        Assert.False((await UserAsync(app, "bob"))!.IsAdministrator);
        Assert.Empty(await GroupsOfAsync(app, "bob"));
        using (await browser.PostAsync("account/logout", new Dictionary<string, string>())) { }

        // carol is an administrator in a group who signs in through "plain" without any claim: untouched.
        await CreateUserAsync(app, "carol", administrator: true);
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
            var carol = (await users.FindByNameAsync("carol"))!;
            await users.AddLoginAsync(carol, new UserLoginInfo("plain", "p-carol", "plain"));
            await scope.ServiceProvider.GetRequiredService<IAccessAdministration>().AddGroupMemberAsync("Developers", carol.Id, "test", TestContext.Current.CancellationToken);
        }

        using (await SignInAsync(browser, "plain", "p-carol", "carol", extra: "c=roles=none")) { }
        Assert.True((await UserAsync(app, "carol"))!.IsAdministrator);
        Assert.Equal(new[] { "Developers" }, await GroupsOfAsync(app, "carol"));
        using (await browser.PostAsync("account/logout", new Dictionary<string, string>())) { }

        // A user who signs in through "corp" is mapped; carol, who never does, is not.
        using (await SignInAsync(browser, "corp", "c-1", "dora", extra: "c=roles=baget-admin")) { }
        Assert.True((await UserAsync(app, "dora"))!.IsAdministrator);
        Assert.True((await UserAsync(app, "carol"))!.IsAdministrator);
    }

    [Fact]
    public async Task GroupMappingsAddAndRemoveOnlyTheMappedGroupsAndSkipUnknownOnes()
    {
        var audit = new RecordingAudit();
        var (app, browser) = Start(Mapped("corp"), audit);
        await CreateGroupAsync(app, "Developers");
        await CreateGroupAsync(app, "Testers");
        await CreateGroupAsync(app, "Unmapped"); // no mapping names it: never touched
        // "Operators" is mapped but does not exist: skipped, not created.

        using (await SignInAsync(browser, "corp", "c-1", "alice", extra: "c=groups=dev&c=groups=ops&c=groups=qa&c=groups=unrelated")) { }
        using (var scope = app.Services.CreateScope())
        {
            var alice = (await scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>().FindByNameAsync("alice"))!;
            await scope.ServiceProvider.GetRequiredService<IAccessAdministration>().AddGroupMemberAsync("Unmapped", alice.Id, "test", TestContext.Current.CancellationToken);
        }

        Assert.Equal(new[] { "Developers", "Testers", "Unmapped" }, (await GroupsOfAsync(app, "alice")).OrderBy(x => x));
        using (await browser.PostAsync("account/logout", new Dictionary<string, string>())) { }

        // qa is gone from the claim, dev stays, ops still names a group that does not exist.
        using (await SignInAsync(browser, "corp", "c-1", "alice", extra: "c=groups=dev&c=groups=ops")) { }

        Assert.Equal(new[] { "Developers", "Unmapped" }, (await GroupsOfAsync(app, "alice")).OrderBy(x => x));
        Assert.DoesNotContain(await WithAsync(app, s => s.GetRequiredService<IAccessAdministration>().ListGroupsAsync(TestContext.Current.CancellationToken)), g => g.Name == "Operators");
        var entries = await WithAsync(app, s => s.GetRequiredService<IAccessAdministration>().AuditAsync(100, TestContext.Current.CancellationToken));
        Assert.Contains(entries, e => e.Actor == "provider:corp" && e.Action == "group.member.add" && e.Subject == "Developers");
        Assert.Contains(entries, e => e.Actor == "provider:corp" && e.Action == "group.member.remove" && e.Subject == "Testers");
        Assert.DoesNotContain(entries, e => e.Actor == "provider:corp" && e.Subject == "Unmapped");
    }

    [Fact]
    public async Task AClaimSentUnderTheDotNetTypeNameIsFoundToo()
    {
        var settings = Mapped("corp");
        settings[P + "corp:AdministratorClaim"] = "role"; // the handler maps "role" to the .NET role claim type
        var (app, browser) = Start(settings);
        await CreateUserAsync(app, "boss", administrator: true);

        using (await SignInAsync(browser, "corp", "c-1", "alice", extra: "c=" + Uri.EscapeDataString(ClaimTypes.Role) + "=baget-admin")) { }

        Assert.True((await UserAsync(app, "alice"))!.IsAdministrator);
    }

    [Theory]
    [InlineData("roles", "roles")]
    [InlineData("role", "roles")]
    [InlineData("roles", "role")]
    [InlineData("role", "role")]
    [InlineData("group", "group")]
    [InlineData("groups", "groups")]
    public async Task AdministratorAndGroupClaimsMatchWhateverTheHandlerRenamedThem(string configured, string sent)
    {
        var settings = Mapped("corp");
        settings[P + "corp:AdministratorClaim"] = configured;
        settings[P + "corp:GroupClaim"] = configured;
        var (app, browser) = Start(settings);
        await CreateGroupAsync(app, "Developers");

        // The claim reaches the application under the type the handler mapped it to.
        using (await SignInAsync(browser, "corp", "c-1", "alice", extra: $"c={sent}=baget-admin&c={sent}=dev"))
        {
        }

        var alice = (await UserAsync(app, "alice"))!;
        // role/roles are the same claim type after mapping; group is a different one from groups.
        var sameClaim = configured is "role" or "roles" ? sent is "role" or "roles" : configured == sent;
        Assert.Equal(sameClaim, alice.IsAdministrator);
        Assert.Equal(sameClaim, (await GroupsOfAsync(app, "alice")).Contains("Developers"));
    }

    [Fact]
    public void ExternalClaimsFindsAClaimByItsRawNameOrTheTypeTheHandlerMapsItTo()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "r1"),
            new Claim(ClaimTypes.Email, "a@b.c"),
            new Claim("groups", "g1"),
            new Claim("groups", "g2"),
        }, "test"));

        Assert.Equal(new[] { "r1" }, ExternalClaims.Values(principal, "roles"));
        Assert.Equal(new[] { "r1" }, ExternalClaims.Values(principal, "role"));
        Assert.Equal(new[] { "r1" }, ExternalClaims.Values(principal, ClaimTypes.Role));
        Assert.Equal("a@b.c", ExternalClaims.First(principal, "email"));
        Assert.Equal(new[] { "g1", "g2" }, ExternalClaims.Values(principal, "groups"));
        Assert.Empty(ExternalClaims.Values(principal, "group"));
        Assert.Empty(ExternalClaims.Values(principal, null));
    }

    [Fact]
    public async Task TheMappingIsNotAppliedWhileTheServerIsReadOnlyButTheSignInStillWorks()
    {
        var (app, browser) = Start(Mapped("corp"));
        using (await SignInAsync(browser, "corp", "c-1", "alice", extra: "c=roles=none")) { }
        using (await browser.PostAsync("account/logout", new Dictionary<string, string>())) { }
        app.Services.GetRequiredService<MaintenanceGate>().Close("maintenance");

        using (var again = await SignInAsync(browser, "corp", "c-1", "alice", extra: "c=roles=baget-admin")) Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);

        Assert.False((await UserAsync(app, "alice"))!.IsAdministrator);
        Assert.True(await IsSignedInAsync(browser));
    }

    [Fact]
    public async Task AFirstSignInWithAnAdministratorClaimCreatesAnApprovedAdministratorEvenWhenApprovalIsRequired()
    {
        var settings = Mapped("corp");
        settings["Accounts:RequireApproval"] = "true";
        var (app, browser) = Start(settings);

        using (var created = await SignInAsync(browser, "corp", "c-1", "alice", extra: "c=roles=baget-admin")) Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        var alice = (await UserAsync(app, "alice"))!;
        Assert.True(alice.IsAdministrator);
        Assert.False(alice.PendingApproval);
    }
}
