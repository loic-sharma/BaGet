using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;
using BaGet.Identity.Application.Email;
using BaGet.Identity.Application.RateLimiting;

namespace BaGet.Tests;

/// <summary>
/// Links in account emails must never come from an attacker-controlled Host header, and
/// "forgot password" must not reveal which accounts exist.
/// </summary>
public class EmailLinkTests : IDisposable
{
    private const string Password = "Passw0rd!";

    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public EmailLinkTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }

    private BaGetApplication Start(Dictionary<string, string>? extra = null, Action<IServiceCollection>? configure = null)
    {
        var settings = new Dictionary<string, string>
        {
            { "Accounts:Enabled", "true" },
            { "ApiKey", "" },
            { "Email:Smtp:Host", "smtp.example.com" },
            { "Email:Smtp:From", "baget@example.com" },
        };
        foreach (var pair in extra ?? new Dictionary<string, string>()) settings[pair.Key] = pair.Value;

        var app = new BaGetApplication(_output, configOverrides: settings, configureServices: configure);
        _disposables.Add(app);

        return app;
    }

    private Browser NewBrowser(BaGetApplication app, string? host = null)
    {
        var browser = new Browser(app, host);
        _disposables.Add(browser);

        return browser;
    }

    private static async Task CreateConfirmedUserAsync(BaGetApplication app, string name, string email)
    {
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
        var result = await users.CreateAsync(
            new BaGetUser { UserName = name, Email = email, EmailConfirmed = true, Created = DateTime.UtcNow },
            Password);
        Assert.True(result.Succeeded);
    }

    private static Task<HttpResponseMessage> ForgotAsync(Browser browser, string identifier)
        => browser.PostAsync("account/forgotpassword", new Dictionary<string, string> { { "Identifier", identifier } });

    private static async Task<IReadOnlyList<CapturedEmail>> ResetMailsAsync(BaGetApplication app, int expected)
    {
        var sender = app.Services.GetRequiredService<CapturingEmailSender>();
        for (var i = 0; i < 100 && sender.Sent.Count(m => m.Kind == "reset-link") < expected; i++) await Task.Delay(50);

        return sender.Sent.Where(m => m.Kind == "reset-link").ToList();
    }

    // --- S1: where links point ----------------------------------------------------------

    [Fact]
    public async Task WithNoPublicUrlAndUnrestrictedHostsEmailIsSwitchedOff()
    {
        var app = Start();
        var browser = NewBrowser(app, host: "evil.example");

        using var forgot = await browser.GetAsync("account/forgotpassword");
        Assert.Equal(HttpStatusCode.NotFound, forgot.StatusCode);
        Assert.False(app.Services.GetRequiredService<IOptions<EmailOptions>>().Value.IsConfigured);

        // Registration then works as it does without email: nothing is mailed, no address needed.
        await browser.RegisterAsync("alice");
        Assert.Empty(app.Services.GetRequiredService<CapturingEmailSender>().Sent);
    }

    [Fact]
    public async Task LinksAreBuiltFromThePublicUrlNotTheRequestHost()
    {
        var app = Start(new() { { "Site:PublicUrl", "https://baget.example.com/feed/" } });
        await CreateConfirmedUserAsync(app, "alice", "alice@example.com");
        var browser = NewBrowser(app, host: "evil.example");

        using (await ForgotAsync(browser, "alice")) { }

        var mail = Assert.Single(await ResetMailsAsync(app, 1));
        Assert.StartsWith("https://baget.example.com/feed/account/resetpassword?", mail.Payload);
        Assert.DoesNotContain("evil.example", mail.Payload);

        using var registered = await browser.PostAsync("account/register", new Dictionary<string, string>
        {
            { "UserName", "bob" },
            { "Email", "bob@example.com" },
            { "Password", Password },
            { "ConfirmPassword", Password },
        });
        var confirmation = app.Services.GetRequiredService<CapturingEmailSender>().Sent.Last(m => m.Kind == "confirmation");
        Assert.StartsWith("https://baget.example.com/feed/account/confirmemail?", confirmation.Payload);
    }

    [Fact]
    public async Task WithRestrictedHostsButNoPublicUrlEmailIsStillSwitchedOff()
    {
        var app = Start(new() { { "AllowedHosts", "baget.example.com" } });
        await CreateConfirmedUserAsync(app, "alice", "alice@example.com");
        var browser = NewBrowser(app, host: "baget.example.com");

        using var forgot = await browser.GetAsync("account/forgotpassword");

        Assert.Equal(HttpStatusCode.NotFound, forgot.StatusCode);
        Assert.False(app.Services.GetRequiredService<IOptions<EmailOptions>>().Value.IsConfigured);
    }

    [Fact]
    public async Task ASpoofedForwardedProtoCannotDowngradeALink()
    {
        var app = Start(new() { { "Site:PublicUrl", "https://baget.example.com" }, { "AllowedHosts", "baget.example.com" } });
        await CreateConfirmedUserAsync(app, "alice", "alice@example.com");
        var browser = NewBrowser(app, host: "baget.example.com");

        using var request = new HttpRequestMessage(HttpMethod.Post, "account/forgotpassword")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "Identifier", "alice" },
                { "__RequestVerificationToken", await browser.TokenAsync("account/forgotpassword") },
            }),
        };
        request.Headers.Add("X-Forwarded-Proto", "http");
        using (await browser.SendAsync(request)) { }

        var mail = Assert.Single(await ResetMailsAsync(app, 1));
        Assert.StartsWith("https://baget.example.com/account/resetpassword?", mail.Payload);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("baget.example.com")]
    [InlineData("ftp://baget.example.com")]
    [InlineData("javascript:alert(1)")]
    public void AnInvalidPublicUrlFailsStartup(string publicUrl)
    {
        var app = Start(new() { { "Site:PublicUrl", publicUrl } });

        Assert.ThrowsAny<Exception>(() => app.CreateClient());
    }

    [Theory]
    [InlineData("https://baget.example.com", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("ftp://x", false)]
    public void LinksCanBeBuiltOnlyFromAPublicUrl(string? publicUrl, bool expected)
        => Assert.Equal(expected, EmailLinkPolicy.CanBuildLinks(publicUrl));

    // --- S5: forgot password ------------------------------------------------------------

    [Fact]
    public async Task TheResponseDoesNotWaitForTheMailToBeSent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = new TaskCompletionSource();
        var app = Start(
            new() { { "Site:PublicUrl", "https://baget.example.com" } },
            services =>
            {
                services.RemoveAll<IEmailSender<BaGetUser>>();
                services.AddSingleton<IEmailSender<BaGetUser>>(new BlockingSender(release.Task));
            });
        await CreateConfirmedUserAsync(app, "alice", "alice@example.com");
        var browser = NewBrowser(app);

        // Warm the host up first, so the timeout below measures the POST alone and not a
        // cold start under a loaded test run.
        using (await browser.GetAsync("account/forgotpassword")) { }

        var post = ForgotAsync(browser, "alice");
        var winner = await Task.WhenAny(post, Task.Delay(TimeSpan.FromSeconds(10), cancellationToken));
        release.SetResult();

        Assert.Same(post, winner);
        using var response = await post;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ASecondRequestForTheSameAccountIsAnsweredTheSameButNotMailed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var app = Start(new() { { "Site:PublicUrl", "https://baget.example.com" } });
        await CreateConfirmedUserAsync(app, "alice", "alice@example.com");
        var browser = NewBrowser(app);

        using var first = await ForgotAsync(browser, "alice");
        await ResetMailsAsync(app, 1);
        using var second = await ForgotAsync(browser, "alice@example.com");
        await Task.Delay(300, cancellationToken);

        Assert.Equal(first.StatusCode, second.StatusCode);
        Assert.Single(await ResetMailsAsync(app, 1));
    }

    [Fact]
    public void TheLimiterAllowsOneMailPerTargetPerWindowAndSixtyRequestsPerClient()
    {
        var time = new MutableTime();
        var limiter = new PasswordResetRateLimiter(time);

        Assert.True(limiter.TryAcquire("client", "alice"));
        Assert.False(limiter.TryAcquire("client", "alice"));
        Assert.False(limiter.TryAcquire("other-client", "alice"));

        time.Now += PasswordResetRateLimiter.Window;
        Assert.True(limiter.TryAcquire("client", "alice"));

        for (var i = 0; i < PasswordResetRateLimiter.MaxPerClient - 1; i++)
        {
            Assert.True(limiter.TryAcquire("client", "target" + i));
        }

        Assert.False(limiter.TryAcquire("client", "one-too-many"));
        Assert.True(limiter.TryAcquire("another-client", "one-too-many"));

        time.Now += PasswordResetRateLimiter.Window;
        Assert.True(limiter.TryAcquire("client", "one-too-many"));
    }

    [Fact]
    public void ASharedOrSpoofedClientAddressNeverBlocksTheFirstResetOfAnAccountSiteWide()
    {
        var time = new MutableTime();
        var limiter = new PasswordResetRateLimiter(time);

        // Every request behind a proxy that sets no forwarded header shares one address.
        for (var i = 0; i < PasswordResetRateLimiter.MaxPerClient; i++)
        {
            Assert.True(limiter.TryAcquire("", "account" + i));
        }

        // The shared address is spent; an account that comes from a different address still gets its mail.
        Assert.True(limiter.TryAcquire("203.0.113.9", "someone-else"));
    }

    [Fact]
    public void ThereIsNoSiteWideCapSoAFloodNeverBlocksAnotherAccount()
    {
        var time = new MutableTime();
        var limiter = new PasswordResetRateLimiter(time);

        // A flood from distinct (spoofed) addresses over many public account names.
        for (var i = 0; i < 5000; i++) Assert.True(limiter.TryAcquire("ip" + i, "account" + i));

        // Nobody else is blocked, and repeating a name still is.
        Assert.True(limiter.TryAcquire("fresh-ip", "victim"));
        Assert.False(limiter.TryAcquire("fresh-ip-2", "account0"));
    }

    [Fact]
    public void TheStateIsBoundedByTheAccountsAndForgottenAfterTheWindow()
    {
        var time = new MutableTime();
        var limiter = new PasswordResetRateLimiter(time);
        const int accounts = 200;

        // Repeated requests for the same accounts, from ever new addresses, record nothing more.
        for (var round = 0; round < 50; round++)
        {
            for (var i = 0; i < accounts; i++) limiter.TryAcquire("ip" + round + "-" + i, "account" + i);
        }

        Assert.Equal(accounts, StateSize(limiter));

        time.Now += PasswordResetRateLimiter.Window;
        Assert.True(limiter.TryAcquire("fresh-ip", "account0"));
        Assert.Equal(1, StateSize(limiter));
    }

    private static int StateSize(PasswordResetRateLimiter limiter) => limiter.TrackedCount;

    [Fact]
    public async Task RequestsForUnknownNamesUseUpNoAllowance()
    {
        var app = Start(new() { { "Site:PublicUrl", "https://baget.example.com" } });
        await CreateConfirmedUserAsync(app, "alice", "alice@example.com");
        var browser = NewBrowser(app);

        for (var i = 0; i < PasswordResetRateLimiter.MaxPerClient + 5; i++)
        {
            using (await ForgotAsync(browser, "nobody" + i)) { }
        }

        using (await ForgotAsync(browser, "alice")) { }

        Assert.Single(await ResetMailsAsync(app, 1));
    }

    private sealed class MutableTime : SystemTime
    {
        public DateTime Now { get; set; } = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public override DateTime UtcNow => Now;
    }

    private sealed class BlockingSender : IEmailSender<BaGetUser>
    {
        private readonly Task _gate;

        public BlockingSender(Task gate) => _gate = gate;

        public Task SendConfirmationLinkAsync(BaGetUser user, string email, string confirmationLink) => _gate;

        public Task SendPasswordResetLinkAsync(BaGetUser user, string email, string resetLink) => _gate;

        public Task SendPasswordResetCodeAsync(BaGetUser user, string email, string resetCode) => _gate;
    }
}
