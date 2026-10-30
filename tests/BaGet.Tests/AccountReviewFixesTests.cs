using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// Regressions found in review of the account features: re-authentication for sensitive
/// changes, sessions of deleted users, and recovery with email on.
/// </summary>
public class AccountReviewFixesTests : IDisposable
{
    private const string Password = "Passw0rd!";

    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public AccountReviewFixesTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }

    private BaGetApplication Start(bool email = false)
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

        return app;
    }

    private Browser NewBrowser(BaGetApplication app)
    {
        var browser = new Browser(app);
        _disposables.Add(browser);

        return browser;
    }

    private static async Task<BaGetUser?> FindAsync(BaGetApplication app, string name)
    {
        using var scope = app.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>().FindByNameAsync(name);
    }

    // --- S2: change email re-authenticates ----------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    public async Task ChangingTheEmailNeedsTheCurrentPassword(string? password)
    {
        var app = Start();
        var browser = NewBrowser(app);
        await browser.RegisterAsync("alice");
        var fields = new Dictionary<string, string> { { "newEmail", "attacker@example.com" } };
        if (password != null) fields["password"] = password;

        using var response = await browser.PostAsync("account/settings", fields, action: "account/settings?handler=ChangeEmail");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        #pragma warning disable CS8602
        Assert.Null((await FindAsync(app, "alice")).Email);
        #pragma warning restore CS8602
    }

    [Fact]
    public async Task ChangingTheEmailWorksWithTheCurrentPassword()
    {
        var app = Start();
        var browser = NewBrowser(app);
        await browser.RegisterAsync("alice");

        using var response = await browser.PostAsync("account/settings", new Dictionary<string, string>
        {
            { "newEmail", "alice@example.com" },
            { "password", Password },
        }, action: "account/settings?handler=ChangeEmail");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        #pragma warning disable CS8602
        Assert.Equal("alice@example.com", (await FindAsync(app, "alice")).Email);
        #pragma warning restore CS8602
    }

    // --- S3: a deleted user's other sessions stop working -------------------------------

    [Fact]
    public async Task AnotherSessionOfADeletedUserIsAnonymousAtOnce()
    {
        var app = Start();
        var first = NewBrowser(app);
        await first.RegisterAsync("alice");
        var second = NewBrowser(app);
        using (var login = await second.PostAsync("account/login", new Dictionary<string, string>
        {
            { "UserName", "alice" },
            { "Password", Password },
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        }

        Assert.Contains("account/settings", await second.GetPageAsync(""));

        using (var deleted = await first.PostAsync("account/settings", new Dictionary<string, string> { { "password", Password } },
                   action: "account/settings?handler=Delete"))
        {
            Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        }

        Assert.Null(await FindAsync(app, "alice"));
        Assert.DoesNotContain("account/settings", await second.GetPageAsync(""));
        using var settings = await second.GetAsync("account/settings");
        Assert.Equal(HttpStatusCode.Redirect, settings.StatusCode);
    }

    // --- S7: the command line recovery also confirms the address ------------------------

    [Fact]
    public async Task ResettingAPasswordConfirmsTheUsersAddressSoTheyCanSignInWithEmailOn()
    {
        var app = Start(email: true);
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
            Assert.True((await users.CreateAsync(
                new BaGetUser { UserName = "alice", Email = "a@example.com", Created = DateTime.UtcNow },
                Password)).Succeeded);
        }

        string? password;
        using (var scope = app.Services.CreateScope())
        {
            password = await scope.ServiceProvider.GetRequiredService<AccountRecoveryService>()
                .ResetPasswordAsync("alice", CancellationToken.None);
        }

        #pragma warning disable CS8602
        Assert.True((await FindAsync(app, "alice")).EmailConfirmed);
        #pragma warning restore CS8602
        var browser = NewBrowser(app);
        #pragma warning disable CS8604
        using var login = await browser.PostAsync("account/login", new Dictionary<string, string>
        {
            { "UserName", "alice" },
            { "Password", password },
        });
        #pragma warning restore CS8604
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
    }

    [Fact]
    public async Task ResettingAPasswordWithAnAddressSetsAndConfirmsIt()
    {
        var app = Start(email: true);
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
            Assert.True((await users.CreateAsync(new BaGetUser { UserName = "alice", Created = DateTime.UtcNow }, Password)).Succeeded);
        }

        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AccountRecoveryService>()
                .ResetPasswordAsync("alice", CancellationToken.None, email: "alice@example.com");
        }

        var alice = await FindAsync(app, "alice");
        #pragma warning disable CS8602
        Assert.Equal("alice@example.com", alice.Email);
        #pragma warning restore CS8602
        Assert.True(alice.EmailConfirmed);
    }
}
