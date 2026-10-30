using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Models.Users;

namespace BaGet.Tests;

/// <summary>
/// The per-row actions of the administrator's Users page: package link, password reset
/// (generated or mailed) and the delete link. None applies to the administrator's own row.
/// </summary>
public class AdminUserActionsTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public AdminUserActionsTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }

    private async Task<(BaGetApplication App, Browser Admin)> StartAsync(bool email = false)
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

        var app = new BaGetApplication(_output, configOverrides: settings);
        _disposables.Add(app);

        await CreateUserAsync(app, "root", administrator: true, email: email ? "root@example.com" : null);
        var admin = new Browser(app);
        _disposables.Add(admin);
        await SignInAsync(admin, "root");

        return (app, admin);
    }

    private static Task<HttpResponseMessage> PostAsync(Browser admin, string handler, string userName, string password = Password)
        => admin.PostAsync("admin/users", new Dictionary<string, string>
        {
            { "userName", userName },
            { "password", password },
        }, action: $"admin/users?handler={handler}");

    private static string GeneratedPassword(string html)
        => WebUtility.HtmlDecode(Regex.Match(html, "<code id=\"generated-password\">(.*?)</code>").Groups[1].Value);

    [Fact]
    public async Task EveryOtherRowHasPackagesResetAndDeleteButTheOwnRowHasNone()
    {
        var (app, admin) = await StartAsync();
        await CreateUserAsync(app, "alice");

        var html = await admin.GetPageAsync("admin/users");

        Assert.Contains("/admin/users/alice/packages", html);
        Assert.Contains("/admin/users/alice/delete", html);
        Assert.Single(Regex.Matches(html, "handler=ResetPassword"));
        Assert.Single(Regex.Matches(html, "/delete\""));
        Assert.DoesNotContain("/admin/users/root/", html);
    }

    [Fact]
    public async Task GeneratingAPasswordReplacesItEndsSessionsSwitchesTwoFactorOffAndIsNotCached()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (app, admin) = await StartAsync();
        await CreateUserAsync(app, "alice");
        using var alice = new Browser(app);
        await SignInAsync(alice, "alice");
        await WithAsync(app, async p =>
        {
            var users = p.GetRequiredService<UserManager<BaGetUser>>();
            var user = await users.FindByNameAsync("alice");
            #pragma warning disable CS8604
            await users.SetTwoFactorEnabledAsync(user, true);
            #pragma warning restore CS8604

            return true;
        });

        using var response = await PostAsync(admin, "ResetPassword", "alice");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        #pragma warning disable CS8602
        Assert.Contains("no-store", response.Headers.CacheControl.ToString());
        #pragma warning restore CS8602
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var generated = GeneratedPassword(html);
        Assert.NotEmpty(generated);

        // Neither a cookie (TempData) nor the page that follows keeps it.
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            Assert.DoesNotContain(cookies, c => c.Contains(generated) || c.Contains(Uri.EscapeDataString(generated)));
        }

        Assert.DoesNotContain("generated-password", await admin.GetPageAsync("admin/users"));

        Assert.False(await CanSignInAsync(app, "alice", Password));
        Assert.True(await CanSignInAsync(app, "alice", generated));
        #pragma warning disable CS8602
        Assert.False((await FindAsync(app, "alice")).TwoFactorEnabled);
        #pragma warning restore CS8602

        // The user's earlier session ended with the new security stamp.
        using var stale = await alice.GetAsync("account/settings");
        Assert.NotEqual(HttpStatusCode.OK, stale.StatusCode);
    }

    [Fact]
    public async Task AResetNeedsTheAdministratorsPassword()
    {
        var (app, admin) = await StartAsync();
        await CreateUserAsync(app, "alice");

        using var response = await PostAsync(admin, "ResetPassword", "alice", password: "wrong");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("confirmation is incorrect", await admin.GetPageAsync("admin/users"));
        Assert.True(await CanSignInAsync(app, "alice", Password));
    }

    [Theory]
    [InlineData("ResetPassword")]
    [InlineData("SendResetLink")]
    public async Task AdministratorsOwnPasswordIsNotResetFromThisPage(string handler)
    {
        var (app, admin) = await StartAsync(email: true);

        using var response = await PostAsync(admin, handler, "root");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Reset your own password", await admin.GetPageAsync("admin/users"));
        Assert.True(await CanSignInAsync(app, "root", Password));
    }

    [Fact]
    public async Task AResetOfAnUnknownUserIsReported()
    {
        var (_, admin) = await StartAsync();

        using var response = await PostAsync(admin, "ResetPassword", "nobody");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("There is no user with that name.", await admin.GetPageAsync("admin/users"));
    }

    [Fact]
    public async Task ResettingCanSetAndConfirmTheEmailAddressLikeTheCommandDoes()
    {
        var (app, admin) = await StartAsync();
        await CreateUserAsync(app, "alice");

        using var response = await admin.PostAsync("admin/users", new Dictionary<string, string>
        {
            { "userName", "alice" },
            { "password", Password },
            { "resetEmail", "alice@example.com" },
        }, action: "admin/users?handler=ResetPassword");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var alice = await FindAsync(app, "alice");
        Assert.Equal("alice@example.com", alice?.Email);
        Assert.True(alice?.EmailConfirmed);
    }

    [Fact]
    public async Task ANonAdministratorCannotReset()
    {
        var (app, _) = await StartAsync();
        await CreateUserAsync(app, "alice");
        await CreateUserAsync(app, "bob");
        using var alice = new Browser(app);
        await SignInAsync(alice, "alice");

        using var response = await alice.PostAsync("account/settings", new Dictionary<string, string>
        {
            { "userName", "bob" },
            { "password", Password },
        }, action: "admin/users?handler=ResetPassword");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(await CanSignInAsync(app, "bob", Password));
    }

    [Fact]
    public async Task TheEmailLinkIsSentThroughTheEmailSenderAndOffersNoPasswordOnScreen()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (app, admin) = await StartAsync(email: true);
        await CreateUserAsync(app, "alice", email: "alice@example.com");

        Assert.Contains("SendResetLink", await admin.GetPageAsync("admin/users"));

        using var response = await PostAsync(admin, "SendResetLink", "alice");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Reset link sent to a***@example.com", await admin.GetPageAsync("admin/users"));

        var sender = app.Services.GetRequiredService<CapturingEmailSender>();
        for (var i = 0; i < 100 && !sender.Sent.Any(m => m.Kind == "reset-link"); i++) await Task.Delay(50, cancellationToken);

        var mail = Assert.Single(sender.Sent, m => m.Kind == "reset-link");
        Assert.Equal("alice@example.com", mail.To);
        Assert.StartsWith("https://baget.example.com/account/resetpassword?", mail.Payload);
        Assert.True(await CanSignInAsync(app, "alice", Password));
    }

    [Fact]
    public async Task TheEmailLinkAndTheDeletionAreRefusedWhileTheGateIsClosed()
    {
        var (app, admin) = await StartAsync(email: true);
        await CreateUserAsync(app, "alice", email: "alice@example.com");
        Assert.Contains("SendResetLink", await admin.GetPageAsync("admin/users")); // token taken while the gate is open
        app.Services.GetRequiredService<MaintenanceGate>().CloseUntilRestart("test");

        using (var send = await PostAsync(admin, "SendResetLink", "alice"))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, send.StatusCode);
        }

        using (var sendWithoutName = await PostAsync(admin, "SendResetLink", ""))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, sendWithoutName.StatusCode);
        }

        using (var delete = await admin.PostAsync("admin/users", new Dictionary<string, string> { { "password", Password } },
            action: "admin/users/alice/delete"))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, delete.StatusCode);
        }

        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(app.Services.GetRequiredService<CapturingEmailSender>().Sent, m => m.Kind == "reset-link");
        Assert.True(await CanSignInAsync(app, "alice", Password));
    }

    [Fact]
    public async Task TheEmailLinkNeedsAnAddress()
    {
        var (app, admin) = await StartAsync(email: true);
        await CreateUserAsync(app, "alice");

        using var response = await PostAsync(admin, "SendResetLink", "alice");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("has no email address", await admin.GetPageAsync("admin/users"));
        Assert.DoesNotContain(app.Services.GetRequiredService<CapturingEmailSender>().Sent, m => m.Kind == "reset-link");
    }

    [Fact]
    public async Task TheEmailLinkIsNotFoundWhileEmailIsNotConfigured()
    {
        var (app, admin) = await StartAsync(email: false);
        await CreateUserAsync(app, "alice", email: "alice@example.com");

        Assert.DoesNotContain("SendResetLink", await admin.GetPageAsync("admin/users"));

        using var response = await PostAsync(admin, "SendResetLink", "alice");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void MaskEmailHidesTheLocalPart()
    {
        Assert.Equal("a***@example.com", UsersViewModel.MaskEmail("alice@example.com"));
        Assert.Equal("the user's address", UsersViewModel.MaskEmail("nonsense"));
        Assert.Equal("the user's address", UsersViewModel.MaskEmail(null));
    }
}
