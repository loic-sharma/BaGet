using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// First and repeat sign-ins through an external provider (OpenID Connect), with the
/// provider's answer stood in for by a test endpoint that fills the external cookie the
/// way the provider's callback does.
/// </summary>
public class ExternalLoginTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public ExternalLoginTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }

    private (BaGetApplication App, Browser Browser) Start(bool email)
    {
        var settings = new Dictionary<string, string>
        {
            { "Accounts:Enabled", "true" },
            { "ApiKey", "" },
        };
        if (email)
        {
            settings["Email:Smtp:Host"] = "smtp.example.com";
            settings["Email:Smtp:From"] = "baget@example.com";
            settings["Site:PublicUrl"] = "https://baget.example.com";
        }

        var app = new BaGetApplication(
            _output,
            configOverrides: settings,
            configureServices: services => services.AddTransient<IStartupFilter, ExternalCookieFilter>());
        var browser = new Browser(app);
        _disposables.Add(app);
        _disposables.Add(browser);

        return (app, browser);
    }

    private static async Task ProviderAnswersAsync(Browser browser, string sub, string email, string? verified)
    {
        var query = $"sub={Uri.EscapeDataString(sub)}&email={Uri.EscapeDataString(email)}&name=alice";
        if (verified != null) query += "&verified=" + verified;

        using var answer = await browser.GetAsync("test/external?" + query);
        Assert.Equal(HttpStatusCode.NoContent, answer.StatusCode);
    }

    private static async Task<HttpResponseMessage> CallbackAsync(Browser browser)
        => await browser.GetAsync("account/login?handler=Callback");

    private static async Task<List<BaGetUser>> UsersAsync(BaGetApplication app)
    {
        using var scope = app.Services.CreateScope();

        return scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>().Users.ToList();
    }

    private static async Task<bool> IsSignedInAsync(Browser browser)
        => (await browser.GetPageAsync("")).Contains("account/settings");

    [Fact]
    public async Task AVerifiedProviderEmailCreatesAConfirmedAccountThatIsSignedIn()
    {
        var (app, browser) = Start(email: true);
        await ProviderAnswersAsync(browser, "sub-1", "alice@example.com", "true");

        using var callback = await CallbackAsync(browser);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        var alice = Assert.Single(await UsersAsync(app));
        Assert.True(alice.EmailConfirmed);
        Assert.True(await IsSignedInAsync(browser));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public async Task AnUnverifiedProviderEmailIsNotConfirmedAndIsMailedALinkInsteadOfSigningIn(string? verified)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (app, browser) = Start(email: true);
        await ProviderAnswersAsync(browser, "sub-1", "alice@example.com", verified);

        using var callback = await CallbackAsync(browser);
        var html = await callback.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        Assert.Contains("confirm", html, StringComparison.OrdinalIgnoreCase);
        var alice = Assert.Single(await UsersAsync(app));
        Assert.False(alice.EmailConfirmed);
        Assert.False(await IsSignedInAsync(browser));
        var mail = Assert.Single(app.Services.GetRequiredService<CapturingEmailSender>().Sent);
        Assert.Equal(("confirmation", "alice@example.com"), (mail.Kind, mail.To));
    }

    [Fact]
    public async Task ComingBackBeforeConfirmingNeverCreatesASecondAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (app, browser) = Start(email: true);
        await ProviderAnswersAsync(browser, "sub-1", "alice@example.com", "false");
        using (await CallbackAsync(browser)) { }

        await ProviderAnswersAsync(browser, "sub-1", "alice@example.com", "false");
        using var again = await CallbackAsync(browser);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Contains("confirm", await again.Content.ReadAsStringAsync(cancellationToken), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "alice" }, (await UsersAsync(app)).Select(u => u.UserName));
        Assert.False(await IsSignedInAsync(browser));
    }

    [Fact]
    public async Task AfterFollowingTheLinkTheNextExternalSignInWorks()
    {
        var (app, browser) = Start(email: true);
        await ProviderAnswersAsync(browser, "sub-1", "alice@example.com", "false");
        using (await CallbackAsync(browser)) { }
        var link = new Uri(app.Services.GetRequiredService<CapturingEmailSender>().Sent.Last().Payload).PathAndQuery.TrimStart('/');
        using (var followed = await browser.GetAsync(link))
        {
            Assert.Equal(HttpStatusCode.OK, followed.StatusCode);
        }

        await ProviderAnswersAsync(browser, "sub-1", "alice@example.com", "false");
        using var callback = await CallbackAsync(browser);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Single(await UsersAsync(app));
        Assert.True(await IsSignedInAsync(browser));
    }

    [Fact]
    public async Task WithoutEmailConfiguredTheFirstAndRepeatSignInsWork()
    {
        var (app, browser) = Start(email: false);
        await ProviderAnswersAsync(browser, "sub-1", "alice@example.com", null);
        using (var first = await CallbackAsync(browser))
        {
            Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        }

        await ProviderAnswersAsync(browser, "sub-1", "alice@example.com", null);
        using var second = await CallbackAsync(browser);

        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        Assert.Single(await UsersAsync(app));
        Assert.True(await IsSignedInAsync(browser));
    }

    [Fact]
    public async Task WithEmailConfiguredAProviderWithoutAnAddressCreatesNothing()
    {
        var (app, browser) = Start(email: true);
        await ProviderAnswersAsync(browser, "sub-1", "", "true");

        using var callback = await CallbackAsync(browser);

        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        Assert.Empty(await UsersAsync(app));
    }
}
