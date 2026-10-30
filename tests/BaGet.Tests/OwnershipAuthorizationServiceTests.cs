using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Tests;

/// <summary>
/// The ownership rules of <see cref="OwnershipAuthorizationService"/>, with an
/// administrator's exception: ownership is skipped, the key's scopes and glob are not.
/// </summary>
public class OwnershipAuthorizationServiceTests : IDisposable
{
    private readonly BaGetApplication _app;

    public OwnershipAuthorizationServiceTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "ApiKey", "global-key" },
            { "Accounts:Enabled", "true" },
        });
    }

    public void Dispose() => _app.Dispose();

    private static BaGetPrincipal Principal(bool administrator, string? glob = "*", params string[]? scopes)
        => new("someone-id", "someone", isSuperuser: false, scopes, glob, isAdministrator: administrator);

    private async Task<bool> AuthorizeAsync(BaGetPrincipal? principal, PackageAction action, string? id)
    {
        using var scope = _app.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IPackageAuthorizationService>()
            .AuthorizeAsync(principal, action, id, CancellationToken.None);
    }

    /// <summary>An existing ID with no owner row: it predates accounts.</summary>
    private async Task SeedOwnerlessAsync(string id)
    {
        using var package = TestPackages.Build(id, "1.0.0");
        await _app.AddPackageAsync(package);
    }

    [Fact]
    public async Task AnAdministratorManagesAnIdTheyDoNotOwnWithTheUnlistScope()
    {
        await SeedOwnerlessAsync("Other.Package");

        Assert.True(await AuthorizeAsync(Principal(true, "*", ApiKeyScopes.Unlist), PackageAction.Manage, "Other.Package"));
    }

    [Fact]
    public async Task AnAdministratorStillNeedsTheUnlistScopeToManage()
    {
        await SeedOwnerlessAsync("Other.Package");

        Assert.False(await AuthorizeAsync(Principal(true, "*", ApiKeyScopes.Push), PackageAction.Manage, "Other.Package"));
    }

    [Fact]
    public async Task AnAdministratorStillStaysWithinTheKeysGlob()
    {
        await SeedOwnerlessAsync("Other.Package");

        Assert.False(await AuthorizeAsync(Principal(true, "Mine.*", ApiKeyScopes.Unlist), PackageAction.Manage, "Other.Package"));
        Assert.False(await AuthorizeAsync(Principal(true, "Mine.*", ApiKeyScopes.PushVersion), PackageAction.Push, "Other.Package"));
    }

    [Theory]
    [InlineData(ApiKeyScopes.Push)]
    [InlineData(ApiKeyScopes.PushVersion)]
    public async Task AnAdministratorPushesAVersionOfAnExistingIdWithEitherPushScope(string? scope)
    {
        await SeedOwnerlessAsync("Other.Package");

        #pragma warning disable CS8604
        Assert.True(await AuthorizeAsync(Principal(true, "*", scope), PackageAction.Push, "Other.Package"));
        #pragma warning restore CS8604
    }

    [Fact]
    public async Task AnAdministratorNeedsThePushScopeForANewId()
    {
        Assert.False(await AuthorizeAsync(Principal(true, "*", ApiKeyScopes.PushVersion), PackageAction.Push, "Brand.New"));
        Assert.True(await AuthorizeAsync(Principal(true, "*", ApiKeyScopes.Push), PackageAction.Push, "Brand.New"));
    }

    [Fact]
    public async Task AnAdministratorKeyWithOnlyAnUnrelatedScopeCannotPush()
    {
        await SeedOwnerlessAsync("Other.Package");

        Assert.False(await AuthorizeAsync(Principal(true, "*", ApiKeyScopes.Unlist), PackageAction.Push, "Other.Package"));
    }

    [Fact]
    public async Task ANonAdministratorStillCannotTouchAnIdTheyDoNotOwn()
    {
        await SeedOwnerlessAsync("Other.Package");

        var key = Principal(false, "*", ApiKeyScopes.Push, ApiKeyScopes.PushVersion, ApiKeyScopes.Unlist);

        Assert.False(await AuthorizeAsync(key, PackageAction.Manage, "Other.Package"));
        Assert.False(await AuthorizeAsync(key, PackageAction.Push, "Other.Package"));
    }

    [Fact]
    public async Task ANullPrincipalIsRefused()
        => Assert.False(await AuthorizeAsync(null, PackageAction.Manage, "Other.Package"));
}
