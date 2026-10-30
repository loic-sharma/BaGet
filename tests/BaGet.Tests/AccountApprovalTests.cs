using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// <c>Accounts:RequireApproval</c>: a self-registered account cannot sign in until an administrator approves it on
/// <c>/admin/users</c>; accounts an administrator creates, and servers with the setting off, are unaffected.
/// </summary>
public class AccountApprovalTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public AccountApprovalTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }

    private async Task<(BaGetApplication App, Browser Admin)> StartAsync(bool requireApproval)
    {
        var app = new BaGetApplication(_output, configOverrides: new Dictionary<string, string>
        {
            { "Accounts:Enabled", "true" },
            { "Accounts:RequireApproval", requireApproval ? "true" : "false" },
            { "ApiKey", "" },
        });
        _disposables.Add(app);

        await CreateUserAsync(app, "root", administrator: true);
        var admin = new Browser(app);
        _disposables.Add(admin);
        await SignInAsync(admin, "root");

        return (app, admin);
    }

    private async Task<string> RegisterAsync(BaGetApplication app, string name)
    {
        using var browser = new Browser(app);
        using var response = await browser.PostAsync("account/register", new Dictionary<string, string>
        {
            { "UserName", name },
            { "Password", Password },
            { "ConfirmPassword", Password },
        });

        return response.StatusCode == HttpStatusCode.OK ? await response.Content.ReadAsStringAsync() : string.Empty;
    }

    private static Task<HttpResponseMessage> ApproveAsync(Browser admin, string name)
        => admin.PostAsync("admin/users", new Dictionary<string, string> { { "userName", name }, { "password", Password } },
            action: "admin/users?handler=Approve");

    [Fact]
    public async Task ARegisteredAccountWaitsForApprovalAndCannotSignIn()
    {
        var (app, _) = await StartAsync(requireApproval: true);

        var page = await RegisterAsync(app, "carol");

        Assert.Contains("id=\"awaiting-approval\"", page);
        Assert.True((await FindAsync(app, "carol"))!.PendingApproval);
        Assert.False(await CanSignInAsync(app, "carol", Password));
    }

    [Fact]
    public async Task ThePendingMessageIsOnlyShownForTheRightPassword()
    {
        var (app, _) = await StartAsync(requireApproval: true);
        await RegisterAsync(app, "carol");

        async Task<string> TryAsync(string password)
        {
            using var browser = new Browser(app);
            using var response = await browser.PostAsync("account/login", new Dictionary<string, string> { { "UserName", "carol" }, { "Password", password } });

            return await response.Content.ReadAsStringAsync();
        }

        Assert.Contains("waiting for an administrator", await TryAsync(Password));
        var wrong = await TryAsync("Wr0ng-password");
        Assert.DoesNotContain("waiting for an administrator", wrong);
        Assert.Contains("incorrect", wrong);
    }

    [Fact]
    public async Task ApprovingAnAccountLetsItSignIn()
    {
        var (app, admin) = await StartAsync(requireApproval: true);
        await RegisterAsync(app, "carol");
        Assert.Contains("Awaiting approval", await admin.GetPageAsync("admin/users"));

        using var response = await ApproveAsync(admin, "carol");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("carol was approved", await admin.GetPageAsync("admin/users"));
        Assert.False((await FindAsync(app, "carol"))!.PendingApproval);
        Assert.True(await CanSignInAsync(app, "carol", Password));
    }

    [Fact]
    public async Task ApprovingNeedsTheAdministratorsPassword()
    {
        var (app, admin) = await StartAsync(requireApproval: true);
        await RegisterAsync(app, "carol");

        using var response = await admin.PostAsync("admin/users", new Dictionary<string, string> { { "userName", "carol" }, { "password", "nope" } },
            action: "admin/users?handler=Approve");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True((await FindAsync(app, "carol"))!.PendingApproval);
    }

    [Fact]
    public async Task AnAdministratorCreatedAccountIsNeverPending()
    {
        var (app, admin) = await StartAsync(requireApproval: true);

        using var response = await admin.PostAsync("admin/users", new Dictionary<string, string>
        {
            { "newUserName", "dave" }, { "newPassword", "Sup3rSecret!" }, { "password", Password },
        }, action: "admin/users?handler=Create");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False((await FindAsync(app, "dave"))!.PendingApproval);
        Assert.True(await CanSignInAsync(app, "dave", "Sup3rSecret!"));
    }

    [Fact]
    public async Task WithTheSettingOffARegisteredAccountSignsInAtOnce()
    {
        var (app, admin) = await StartAsync(requireApproval: false);

        using var browser = new Browser(app);
        await browser.RegisterAsync("erin");

        Assert.False((await FindAsync(app, "erin"))!.PendingApproval);
        Assert.DoesNotContain("Awaiting approval", await admin.GetPageAsync("admin/users"));
    }

    [Fact]
    public async Task APendingAccountsKeyNoLongerAuthenticates()
    {
        var (app, _) = await StartAsync(requireApproval: false);
        await CreateUserAsync(app, "frank");
        string key;
        using (var scope = app.Services.CreateScope())
        {
            var user = (await FindAsync(app, "frank"))!;
            key = await scope.ServiceProvider.GetRequiredService<ApiKeyService>()
                .CreateAsync(user.Id, "k", new[] { ApiKeyScopes.Push }, "*", null, System.Threading.CancellationToken.None);
        }

        async Task<bool> AuthenticatesAsync()
        {
            using var scope = app.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IAuthenticationService>()
                .AuthenticateAsync(key, System.Threading.CancellationToken.None);
        }

        Assert.True(await AuthenticatesAsync());

        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<BaGetUser>>();
            var user = (await users.FindByNameAsync("frank"))!;
            user.PendingApproval = true;
            Assert.True((await users.UpdateAsync(user)).Succeeded);
        }

        Assert.False(await AuthenticatesAsync());
    }
}
