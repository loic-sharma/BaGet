using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Models.Shared;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;

namespace BaGet.Tests;

/// <summary>
/// Regressions of the web UI code review: the manage screens do not tell a private repository from a missing one, the admin user
/// actions need a user name and refuse side effects while the read-only gate is closed, the re-authentication return address keeps
/// the path base.
/// </summary>
public class WebUiReviewFixesTests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly Browser _admin;

    public WebUiReviewFixesTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "Accounts:Enabled", "true" },
            { "ApiKey", "" },
        });
        _app.CreateClient().Dispose();
        CreateUserAsync(_app, "root", administrator: true).GetAwaiter().GetResult();
        CreateUserAsync(_app, "bob").GetAwaiter().GetResult();
        _admin = new Browser(_app);
        SignInAsync(_admin, "root").GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _admin.Dispose();
        _app.Dispose();
    }

    [Fact]
    public async Task AManageScreenOfAPrivateRepositoryAnswers404ToACallerWhoCannotReadIt()
    {
        await _app.CreateRepositoryAsync("secret", TestContext.Current.CancellationToken);
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().SetAnonymousReadAsync("secret", false, TestContext.Current.CancellationToken);
        }

        using var bob = new Browser(_app);
        await SignInAsync(bob, "bob");

        using var secret = await bob.GetAsync("r/secret/packages/Some.Package/manage");
        using var missing = await bob.GetAsync("r/nothere/packages/Some.Package/manage");

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, secret.StatusCode);
    }

    [Fact]
    public async Task ResettingWithoutAUserNameSaysThereIsNoSuchUser()
    {
        using var response = await _admin.PostAsync("admin/users", new Dictionary<string, string> { { "password", Password } },
            action: "admin/users?handler=ResetPassword");

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("There is no user with that name.", await _admin.GetPageAsync("admin/users"));
    }

    [Fact]
    public async Task TheAdminUserActionsAreRefusedWhileTheGateIsClosed()
    {
        var page = await _admin.GetPageAsync("admin/users"); // the antiforgery token is taken while the gate is open
        Assert.Contains("bob", page);
        _app.Services.GetRequiredService<MaintenanceGate>().CloseUntilRestart("test");

        foreach (var handler in new[] { "ResetPassword", "Promote", "Demote" })
        {
            using var response = await _admin.PostAsync("admin/users", new Dictionary<string, string> { { "userName", "bob" }, { "password", Password } },
                action: $"admin/users?handler={handler}");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        Assert.True(await CanSignInAsync(_app, "bob", Password));
    }

    [Fact]
    public async Task TheReauthenticationReturnAddressKeepsThePathBase()
    {
        using var scope = _app.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var context = new DefaultHttpContext();
        context.Request.PathBase = "/baget";
        context.Request.Path = "/admin/groups/x";
        context.Request.QueryString = new QueryString("?a=1");
        accessor.HttpContext = context;

        var model = new TestModel();
        await scope.ServiceProvider.GetRequiredService<PasswordConfirmation>().LoadForRequestAsync(model, new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Equal("~/admin/groups/x?a=1", model.ReturnPage);
        Assert.True(LocalUrls.IsLocal(model.ReturnPage));
    }

    [Theory]
    [InlineData("/admin/users/bob", "~/admin/users/bob")]
    [InlineData("/Admin/Settings", "~/Admin/Settings")]
    [InlineData("~/admin/groups/x?a=1", "~/admin/groups/x?a=1")]
    [InlineData("//evil.example", "~/")]
    [InlineData("https://evil.example", "~/")]
    [InlineData(null, "~/")]
    public void AReauthenticationReturnAddressIsWrittenApplicationRelative(string? url, string expected)
        => Assert.Equal(expected, LocalUrls.AppRelative(LocalUrls.Target(url)));

    private sealed class TestModel : ConfirmationModel
    {
    }
}
