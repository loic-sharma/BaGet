using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Services.Accounts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;

namespace BaGet.Tests;

/// <summary>
/// Regression tests of the security review: package-level Read patterns on the V2 by-id endpoints and autocomplete,
/// and the Manage page asking, per handler, the permission the protocol asks for the same change.
/// </summary>
public class SecurityReviewTests : IDisposable
{
    private readonly BaGetApplication _app;

    public SecurityReviewTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "Accounts:Enabled", "true" },
            { "ApiKey", "" },
        });
        _app.CreateClient().Dispose();
    }

    public void Dispose() => _app.Dispose();

    private async Task<T> AdministerAsync<T>(Func<IAccessAdministration, Task<T>> action)
    {
        using var scope = _app.Services.CreateScope();

        return await action(scope.ServiceProvider.GetRequiredService<IAccessAdministration>());
    }

    [Theory]
    [InlineData("api/v2/Packages(Id='Secret.Pkg',Version='1.0.0')")]
    [InlineData("api/v2/FindPackagesById()?id='Secret.Pkg'")]
    [InlineData("api/v2/Packages()?id='Secret.Pkg'")]
    [InlineData("v3/autocomplete?id=Secret.Pkg")]
    public async Task AnExcludedPackageIsHiddenOnTheByIdEndpointsAndAnAllowedOneIsServed(string secretPath)
    {
        await CreateUserAsync(_app, "alice");
        await PushAsync(_app, "alice", "Secret.Pkg");
        await PushAsync(_app, "alice", "Open.Pkg");

        // Everyone reads the default repository except packages matching Secret.*.
        await AdministerAsync(async a =>
        {
            await a.DeleteTargetAsync(BuiltInNames.AnyoneReads, null, CancellationToken.None);
            await a.CreateTargetAsync("public except secret", new[] { "default" }, null, new[] { "Secret.*" }, null, null, CancellationToken.None);
            var everyone = await a.ResolvePrincipalAsync(PrincipalType.Group, BuiltInNames.Everyone, CancellationToken.None);
            await a.SetGrantAsync("public except secret", PrincipalType.Group, everyone!, AccessAction.Read, null, CancellationToken.None);

            return 0;
        });

        using var client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var hidden = await client.GetAsync(secretPath, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, hidden.StatusCode);

        var allowed = secretPath.Replace("Secret.Pkg", "Open.Pkg", StringComparison.Ordinal);
        using var served = await client.GetAsync(allowed, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
    }

    [Theory]
    [InlineData("api/v2/FindPackagesById()?id='Secret.Pkg'&id='x'")]
    [InlineData("api/v2/Packages()?id='Secret.Pkg'&id='x'")]
    [InlineData("v3/autocomplete?id=Secret.Pkg&id=x")]
    public async Task ARepeatedIdQueryKeyDoesNotHideAnExactNameExclusion(string path)
    {
        await CreateUserAsync(_app, "alice");
        await PushAsync(_app, "alice", "Secret.Pkg");

        await AdministerAsync(async a =>
        {
            await a.DeleteTargetAsync(BuiltInNames.AnyoneReads, null, CancellationToken.None);
            await a.CreateTargetAsync("public except secret", new[] { "default" }, null, new[] { "Secret.Pkg" }, null, null, CancellationToken.None);
            var everyone = await a.ResolvePrincipalAsync(PrincipalType.Group, BuiltInNames.Everyone, CancellationToken.None);
            await a.SetGrantAsync("public except secret", PrincipalType.Group, everyone!, AccessAction.Read, null, CancellationToken.None);

            return 0;
        });

        using var client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TheManagePageAsksTheProtocolsPermissionPerHandler()
    {
        await CreateUserAsync(_app, "alice");
        await PushAsync(_app, "alice", "Alice.Package");

        // alice keeps only Manage (owners) and Annotate (deprecation): Delete (unlist) is refused, as the protocol refuses it.
        await AdministerAsync(async a =>
        {
            var alice = await a.ResolvePrincipalAsync(PrincipalType.User, "alice", CancellationToken.None);
            await a.DeleteTargetAsync(BuiltInNames.AuthenticatedDeploy, null, CancellationToken.None);
            await a.CreateTargetAsync("alice manages", new[] { "*" }, null, null, null, null, CancellationToken.None);
            await a.SetGrantAsync("alice manages", PrincipalType.User, alice!, AccessAction.Manage | AccessAction.Annotate, null, CancellationToken.None);

            return 0;
        });
        using var browser = new Browser(_app);
        await SignInAsync(browser, "alice");

        using var page = await browser.GetAsync("packages/Alice.Package/manage");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        using var unlist = await browser.PostAsync("packages/Alice.Package/manage", new Dictionary<string, string>
        {
            { "version", "1.0.0" },
        }, action: "packages/Alice.Package/manage?handler=Unlist");
        Assert.Equal(HttpStatusCode.Forbidden, unlist.StatusCode);

        using var deprecate = await browser.PostAsync("packages/Alice.Package/manage", new Dictionary<string, string>
        {
            { "version", "1.0.0" },
            { "reasons", "Legacy" },
        }, action: "packages/Alice.Package/manage?handler=Deprecate");
        Assert.Contains("is deprecated", await deprecate.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
