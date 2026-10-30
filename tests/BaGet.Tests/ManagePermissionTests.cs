using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;

namespace BaGet.Tests;

/// <summary>
/// The Manage page needs ownership and the access model's Manage permission (D4): the seeded model gives every signed-in
/// user the permission, so owners manage as before; once an administrator removes it, owners lose the page and its link,
/// administrators keep both.
/// </summary>
public class ManagePermissionTests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly Browser _alice;
    private readonly Browser _root;

    public ManagePermissionTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "Accounts:Enabled", "true" },
            { "ApiKey", "" },
        });
        _app.CreateClient().Dispose();
        CreateUserAsync(_app, "alice").GetAwaiter().GetResult();
        CreateUserAsync(_app, "root", administrator: true).GetAwaiter().GetResult();
        PushAsync(_app, "alice", "Alice.Package").GetAwaiter().GetResult();
        _alice = new Browser(_app);
        _root = new Browser(_app);
        SignInAsync(_alice, "alice").GetAwaiter().GetResult();
        SignInAsync(_root, "root").GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _alice.Dispose();
        _root.Dispose();
        _app.Dispose();
    }

    private async Task RevokeManageAsync()
    {
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAccessAdministration>()
            .DeleteTargetAsync(BuiltInNames.AuthenticatedDeploy, null, CancellationToken.None);
    }

    [Fact]
    public async Task AnOwnerManagesWithTheSeededModel()
    {
        using var page = await _alice.GetAsync("packages/Alice.Package/manage");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("/packages/Alice.Package/manage", await _alice.GetPageAsync("packages/Alice.Package"));
    }

    [Fact]
    public async Task AnOwnerWithoutTheManagePermissionLosesThePageAndTheLinkButAnAdministratorKeepsThem()
    {
        await RevokeManageAsync();

        using var page = await _alice.GetAsync("packages/Alice.Package/manage");
        Assert.Equal(HttpStatusCode.Forbidden, page.StatusCode);
        Assert.DoesNotContain("Manage package", await _alice.GetPageAsync("packages/Alice.Package"));

        using var admin = await _root.GetAsync("packages/Alice.Package/manage");
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        Assert.Contains("/packages/Alice.Package/manage", await _root.GetPageAsync("packages/Alice.Package"));
    }
}
