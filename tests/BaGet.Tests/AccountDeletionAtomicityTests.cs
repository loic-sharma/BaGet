using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Tests;

/// <summary>
/// Deleting an account is all or nothing: when the user row cannot be deleted, what hangs
/// off it must still be there.
/// </summary>
public class AccountDeletionAtomicityTests : IDisposable
{
    private readonly BaGetApplication _app;

    public AccountDeletionAtomicityTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "Accounts:Enabled", "true" },
        });
        _app.CreateClient().Dispose();
    }

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task AFailedUserDeleteLeavesKeysAndOrganizationsInPlace()
    {
        string userId;
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
            var alice = new BaGetUser { UserName = "alice", Created = DateTime.UtcNow };
            Assert.True((await users.CreateAsync(alice, "Passw0rd!")).Succeeded);
            userId = alice.Id;

            await scope.ServiceProvider.GetRequiredService<ApiKeyService>()
                .CreateAsync(userId, "k", new[] { ApiKeyScopes.Push }, "*", null, CancellationToken.None);
            Assert.True(await scope.ServiceProvider.GetRequiredService<PackageOwnershipService>()
                .CreateOrganizationAsync("acme", userId, CancellationToken.None));
        }

        // The user as loaded by a request that is about to delete it ...
        using var deleting = _app.Services.CreateScope();
        var stale = await deleting.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>().FindByIdAsync(userId);

        // ... changed by someone else in the meantime, so deleting the stale copy fails.
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
            var fresh = await users.FindByIdAsync(userId);
            #pragma warning disable CS8604
            Assert.True((await users.SetPhoneNumberAsync(fresh, "123")).Succeeded);
            #pragma warning restore CS8604
        }

        var (blockers, result) = await deleting.ServiceProvider.GetRequiredService<AccountDeletionService>()
            .DeleteAsync(stale, CancellationToken.None);

        Assert.False(blockers.Any);
        Assert.False(result.Succeeded);

        using var check = _app.Services.CreateScope();
        Assert.NotNull(await check.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>().FindByIdAsync(userId));
        Assert.Single(await check.ServiceProvider.GetRequiredService<ApiKeyService>().ListAsync(userId, CancellationToken.None));
        #pragma warning disable CS8604
        Assert.Single(await check.ServiceProvider.GetRequiredService<PackageOwnershipService>().GetOrganizationsAsync(userId, CancellationToken.None));
        #pragma warning restore CS8604
    }
}
