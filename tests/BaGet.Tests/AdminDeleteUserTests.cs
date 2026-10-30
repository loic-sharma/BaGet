using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Models.Users;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Tests;

/// <summary>An administrator deleting another account, and what that does to its packages and organizations.</summary>
public class AdminDeleteUserTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public AdminDeleteUserTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }

    private async Task<(BaGetApplication App, Browser Admin)> StartAsync(bool readOnly = false)
    {
        var overrides = new Dictionary<string, string>
        {
            { "Accounts:Enabled", "true" },
            { "ApiKey", "" },
        };
        if (readOnly) overrides["IsReadOnlyMode"] = "true";

        var app = new BaGetApplication(_output, configOverrides: overrides);
        _disposables.Add(app);

        await CreateUserAsync(app, "root", administrator: true);
        var admin = new Browser(app);
        _disposables.Add(admin);
        await SignInAsync(admin, "root");

        return (app, admin);
    }

    private static Task<HttpResponseMessage> DeleteAsync(Browser admin, string userName, string? soleOwned = null, string password = Password)
    {
        var fields = new Dictionary<string, string> { { "password", password } };
        if (soleOwned != null) fields["soleOwned"] = soleOwned;

        return admin.PostAsync($"admin/users/{userName}/delete", fields);
    }

    private static async Task<bool> ExistsAsync(BaGetApplication app, string name) => await FindAsync(app, name) != null;

    [Fact]
    public async Task TheConfirmationPageListsWhatGoes()
    {
        var (app, admin) = await StartAsync();
        await CreateUserAsync(app, "alice");
        await CreateUserAsync(app, "bob");
        await PushAsync(app, "alice", "Alice.Solo");
        await PushAsync(app, "alice", "Alice.Shared");
        await AddOwnerAsync(app, "Alice.Shared", "bob");
        await AddOrganizationAsync(app, "acme", ("alice", true), ("bob", false));

        var html = await admin.GetPageAsync("admin/users/alice/delete");

        Assert.Contains("2 API key(s)", html);
        Assert.Contains("Alice.Shared", html);
        Assert.Contains("Alice.Solo", html);
        Assert.Contains("acme", html);
        Assert.Contains("bob, its longest-standing member, becomes its administrator", html);
        Assert.Contains("name=\"soleOwned\" value=\"keep\" checked", html);
    }

    [Fact]
    public async Task KeepLeavesTheSoleOwnedIdWithoutOwners()
    {
        var (app, admin) = await StartAsync();
        await CreateUserAsync(app, "alice");
        await CreateUserAsync(app, "bob");
        await PushAsync(app, "alice", "Alice.Solo");
        await PushAsync(app, "alice", "Alice.Shared");
        await AddOwnerAsync(app, "Alice.Shared", "bob");

        using var response = await DeleteAsync(admin, "alice", soleOwned: "keep");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(await ExistsAsync(app, "alice"));
        Assert.Empty(await OwnersAsync(app, "Alice.Solo"));
        Assert.NotEmpty(await VersionsAsync(app, "Alice.Solo"));
        Assert.Equal("bob", Assert.Single(await OwnersAsync(app, "Alice.Shared")).OwnerName);

        var users = await admin.GetPageAsync("admin/users");
        Assert.Contains("alice was deleted.", users);
        Assert.Contains("Packages left without owners: Alice.Solo.", users);

        var ownerless = await admin.GetPageAsync("admin/packages");
        Assert.Contains("Alice.Solo", ownerless);
        Assert.DoesNotContain("Alice.Shared", ownerless);
    }

    [Fact]
    public async Task DeleteRemovesEveryVersionOfTheSoleOwnedIdsOnly()
    {
        var (app, admin) = await StartAsync();
        await CreateUserAsync(app, "alice");
        await CreateUserAsync(app, "bob");
        await PushAsync(app, "alice", "Alice.Solo", "1.0.0");
        await PushAsync(app, "alice", "Alice.Solo", "2.0.0");
        await PushAsync(app, "alice", "Alice.Shared");
        await AddOwnerAsync(app, "Alice.Shared", "bob");

        using var response = await DeleteAsync(admin, "alice", soleOwned: "delete");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(await ExistsAsync(app, "alice"));
        Assert.Empty(await VersionsAsync(app, "Alice.Solo"));
        Assert.Empty(await OwnersAsync(app, "Alice.Solo"));
        Assert.Single(await VersionsAsync(app, "Alice.Shared"));
        Assert.Contains("Packages deleted: Alice.Solo.", await admin.GetPageAsync("admin/users"));
    }

    [Fact]
    public async Task ReadOnlyModeRefusesToDeleteTheSoleOwnedPackages()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (app, admin) = await StartAsync(readOnly: true);
        await CreateUserAsync(app, "alice");
        using (var package = TestPackages.Build("Alice.Solo", "1.0.0"))
        {
            await app.AddPackageAsync(package, cancellationToken);
        }
        await AddOwnerAsync(app, "Alice.Solo", "alice");

        var page = await admin.GetPageAsync("admin/users/alice/delete");
        Assert.Contains("disabled", page.Substring(page.IndexOf("value=\"delete\"", StringComparison.Ordinal)).Split('>')[0]);

        using var response = await DeleteAsync(admin, "alice", soleOwned: "delete");

        Assert.Contains(DeleteUserViewModel.ReadOnlyMessage, await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.True(await ExistsAsync(app, "alice"));
        Assert.Single(await VersionsAsync(app, "Alice.Solo"));
        Assert.Single(await OwnersAsync(app, "Alice.Solo"));
    }

    [Fact]
    public async Task ReadOnlyModeStillDeletesTheAccountAndKeepsItsPackages()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (app, admin) = await StartAsync(readOnly: true);
        await CreateUserAsync(app, "alice");
        using (var package = TestPackages.Build("Alice.Solo", "1.0.0"))
        {
            await app.AddPackageAsync(package, cancellationToken);
        }
        await AddOwnerAsync(app, "Alice.Solo", "alice");

        using var response = await DeleteAsync(admin, "alice", soleOwned: "keep");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(await ExistsAsync(app, "alice"));
        Assert.Single(await VersionsAsync(app, "Alice.Solo"));
    }

    [Fact]
    public async Task TheLastAdministratorOfAnOrganizationHandsOverToTheLongestStandingMember()
    {
        var (app, admin) = await StartAsync();
        await CreateUserAsync(app, "alice");
        await CreateUserAsync(app, "bob");
        await CreateUserAsync(app, "carol");
        await AddOrganizationAsync(app, "acme", ("alice", true), ("carol", false), ("bob", false));

        using var response = await DeleteAsync(admin, "alice");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(await ExistsAsync(app, "alice"));

        var members = await WithAsync(app, async p => await p.GetRequiredService<PackageOwnershipService>().GetMembersAsync("acme", CancellationToken.None));
        var carol = await FindAsync(app, "carol");
        var bob = await FindAsync(app, "bob");
        Assert.Equal(2, members.Count);
        #pragma warning disable CS8602
        Assert.True(members.Single(m => m.UserId == carol.Id).IsAdmin);
        Assert.False(members.Single(m => m.UserId == bob.Id).IsAdmin);
        #pragma warning restore CS8602
    }

    [Fact]
    public async Task AnOrganizationThatOwnsPackagesIsKeptWithoutMembers()
    {
        var (app, admin) = await StartAsync();
        await CreateUserAsync(app, "alice");
        await PushAsync(app, "alice", "Acme.Package");
        await AddOrganizationAsync(app, "acme", ("alice", true));
        await AddOwnerAsync(app, "Acme.Package", "acme");

        using var response = await DeleteAsync(admin, "alice");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(await ExistsAsync(app, "alice"));
        Assert.Empty(await WithAsync(app, p => p.GetRequiredService<PackageOwnershipService>().GetMembersAsync("acme", CancellationToken.None)));
        #pragma warning disable CS8604
        Assert.True(await WithAsync(app, p => p.GetRequiredService<IContext>().Organizations.AnyAsync(o => o.Name == "acme")));
        #pragma warning restore CS8604
        Assert.Equal("acme", Assert.Single(await OwnersAsync(app, "Acme.Package")).OwnerName);
    }

    [Fact]
    public async Task AnOrganizationOfOneThatOwnsNothingGoesWithTheUser()
    {
        var (app, admin) = await StartAsync();
        await CreateUserAsync(app, "alice");
        await AddOrganizationAsync(app, "acme", ("alice", true));

        using var response = await DeleteAsync(admin, "alice");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        #pragma warning disable CS8604
        Assert.False(await WithAsync(app, p => p.GetRequiredService<IContext>().Organizations.AnyAsync(o => o.Name == "acme")));
        #pragma warning restore CS8604
    }

    [Fact]
    public async Task TheLastAdministratorCannotBeDeleted()
    {
        var (app, _) = await StartAsync();
        await CreateUserAsync(app, "second", administrator: true);

        // Demote root so that "second" is the only administrator left.
        var succeeded = await WithAsync(app, async p =>
        {
            var users = p.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<BaGetUser>>();
            var second = await users.FindByNameAsync("second");
            var root = await users.FindByNameAsync("root");
            #pragma warning disable CS8602
            root.IsAdministrator = false;
            #pragma warning restore CS8602
            await users.UpdateAsync(root);

            var outcome = await p.GetRequiredService<AccountDeletionService>()
                .DeleteByAdministratorAsync(second, deleteSoleOwnedPackages: false, "root", CancellationToken.None);

            return outcome.Result.Succeeded;
        });

        Assert.False(succeeded);
        Assert.True(await ExistsAsync(app, "second"));
    }

    [Fact]
    public async Task AWrongPasswordDeletesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (app, admin) = await StartAsync();
        await CreateUserAsync(app, "alice");

        using var response = await DeleteAsync(admin, "alice", password: "wrong");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("confirmation is incorrect", await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.True(await ExistsAsync(app, "alice"));
    }

    [Fact]
    public async Task AnAdministratorCannotDeleteThemselvesHere()
    {
        var (app, admin) = await StartAsync();

        using var response = await admin.PostAsync("admin/users", new Dictionary<string, string> { { "password", Password } }, action: "admin/users/root/delete");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(await ExistsAsync(app, "root"));
        Assert.Contains("Delete your own account", await admin.GetPageAsync("admin/users"));
    }

    [Fact]
    public async Task AnUnknownUserIsNotFound()
    {
        var (_, admin) = await StartAsync();

        using var response = await admin.GetAsync("admin/users/nobody/delete");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ANonAdministratorGetsNotFoundAndDeletesNothing()
    {
        var (app, _) = await StartAsync();
        await CreateUserAsync(app, "alice");
        await CreateUserAsync(app, "bob");
        using var alice = new Browser(app);
        await SignInAsync(alice, "alice");

        using var get = await alice.GetAsync("admin/users/bob/delete");
        using var post = await alice.PostAsync("account/settings", new Dictionary<string, string> { { "password", Password } }, action: "admin/users/bob/delete");

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.True(await ExistsAsync(app, "bob"));
    }
}
