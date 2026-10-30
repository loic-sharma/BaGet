using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// U8: the UI offers only what the viewer can do (Manage link, Upload navigation) and the account and administrator
/// pages work across every repository (sole-owned IDs, ownerless IDs), naming and linking each repository.
/// </summary>
public class UiConsistencyTests : IDisposable
{
    private static readonly CancellationToken Cancellation = CancellationToken.None;

    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public UiConsistencyTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }

    private BaGetApplication Start(Dictionary<string, string>? extra = null)
    {
        var overrides = new Dictionary<string, string>
        {
            { "Accounts:Enabled", "true" },
            { "ApiKey", "" },
        };
        foreach (var pair in extra ?? new Dictionary<string, string>()) overrides[pair.Key] = pair.Value;

        var app = new BaGetApplication(_output, configOverrides: overrides);
        _disposables.Add(app);
        app.CreateClient().Dispose();

        return app;
    }

    private async Task<Browser> SignedInAsync(BaGetApplication app, string name)
    {
        var browser = new Browser(app);
        _disposables.Add(browser);
        await SignInAsync(browser, name);

        return browser;
    }

    private static async Task<RepositoryInfo> CreateSecondAsync(BaGetApplication app)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
            .CreateAsync("second", RepositoryFormat.NuGet, RepositoryKind.Local, Cancellation);
    }

    private static IServiceScope Scope(BaGetApplication app, RepositoryInfo repository)
        => app.Services.GetRequiredService<IServiceScopeFactory>().CreateRepositoryScope(repository);

    private static async Task SeedAsync(BaGetApplication app, RepositoryInfo repository, string id, string? owner)
    {
        using var scope = Scope(app, repository);
        var result = await scope.ServiceProvider.GetRequiredService<IPackageDatabase>().AddAsync(
            new Package
            {
                Id = id,
                Version = NuGetVersion.Parse("1.0.0"),
                Listed = true,
                Authors = new[] { "author" },
                Tags = Array.Empty<string>(),
                Description = "A test package.",
                Published = DateTime.UtcNow,
                Dependencies = new List<PackageDependency>(),
                PackageTypes = new List<PackageType> { new() { Name = "Dependency", Version = "0.0" } },
                TargetFrameworks = new List<TargetFramework>(),
            },
            Cancellation);
        Assert.Equal(PackageAddResult.Success, result);

        if (owner == null) return;

        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        context.PackageOwners!.Add(new PackageOwner { PackageId = id, OwnerName = owner });
        await context.SaveChangesAsync(Cancellation);
    }

    // ---- Manage link ----

    [Fact]
    public async Task TheManageLinkIsShownToAnOwner()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        await PushAsync(app, "alice", "Alice.Package");
        var alice = await SignedInAsync(app, "alice");

        var html = await alice.GetPageAsync("packages/Alice.Package");

        Assert.Contains("/packages/Alice.Package/manage", html);
    }

    [Fact]
    public async Task TheManageLinkIsShownToAnAdministratorForAnyId()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        await CreateUserAsync(app, "root", administrator: true);
        await PushAsync(app, "alice", "Alice.Package");
        var root = await SignedInAsync(app, "root");

        var html = await root.GetPageAsync("packages/Alice.Package");

        Assert.Contains("/packages/Alice.Package/manage", html);
    }

    [Fact]
    public async Task TheManageLinkIsHiddenFromASignedInUserWhoCannotManage()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        await CreateUserAsync(app, "bob");
        await PushAsync(app, "alice", "Alice.Package");
        var bob = await SignedInAsync(app, "bob");

        var html = await bob.GetPageAsync("packages/Alice.Package");

        Assert.DoesNotContain("/manage", html);
        Assert.DoesNotContain("Manage package", html);
    }

    [Fact]
    public async Task TheManageLinkIsHiddenWhenSignedOut()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        await PushAsync(app, "alice", "Alice.Package");
        using var browser = new Browser(app);

        var html = await browser.GetPageAsync("packages/Alice.Package");

        Assert.DoesNotContain("Manage package", html);
    }

    // ---- Upload navigation ----

    [Fact]
    public async Task TheUploadNavigationIsShownWhenAccountsAreEnabled()
    {
        var app = Start();
        using var browser = new Browser(app);

        var html = await browser.GetPageAsync("stats");

        Assert.Contains("href=\"/upload\"", html);
    }

    [Fact]
    public async Task TheUploadNavigationIsHiddenWhenNothingCanAuthenticate()
    {
        var app = Start(new Dictionary<string, string> { { "Accounts:Enabled", "false" } });
        using var browser = new Browser(app);

        var html = await browser.GetPageAsync("stats");

        Assert.DoesNotContain("href=\"/upload\"", html);
    }

    [Fact]
    public async Task TheUploadNavigationIsShownWithAGlobalApiKeyAndNoAccounts()
    {
        var app = Start(new Dictionary<string, string> { { "Accounts:Enabled", "false" }, { "ApiKey", "k" } });
        using var browser = new Browser(app);

        var html = await browser.GetPageAsync("stats");

        Assert.Contains("href=\"/upload\"", html);
    }

    [Fact]
    public async Task TheUploadNavigationIsHiddenInReadOnlyMode()
    {
        var app = Start(new Dictionary<string, string> { { "IsReadOnlyMode", "true" } });
        await CreateUserAsync(app, "alice");
        var alice = await SignedInAsync(app, "alice");

        var html = await alice.GetPageAsync("stats");

        Assert.DoesNotContain("href=\"/upload\"", html);
        Assert.DoesNotContain("Upload package", html);
    }

    // ---- Account deletion blockers ----

    [Fact]
    public async Task AccountDeletionBlockersNameAndLinkTheirRepository()
    {
        var app = Start();
        var second = await CreateSecondAsync(app);
        await CreateUserAsync(app, "alice");
        await SeedAsync(app, RepositoryInfo.Default, "Sole.Default", "alice");
        await SeedAsync(app, second, "Sole.Second", "alice");
        var alice = await SignedInAsync(app, "alice");

        using var response = await alice.PostAsync(
            "account/settings",
            new Dictionary<string, string> { { "password", Password } },
            action: "account/settings?handler=Delete");
        var html = await response.Content.ReadAsStringAsync(Cancellation);

        Assert.Contains("href=\"/packages/Sole.Default/manage\"", html);
        Assert.Contains("href=\"/r/second/packages/Sole.Second/manage\"", html);
    }

    [Fact]
    public async Task TheBlockersListEachSoleOwnedIdWithItsRepository()
    {
        var app = Start();
        var second = await CreateSecondAsync(app);
        await CreateUserAsync(app, "alice");
        await SeedAsync(app, RepositoryInfo.Default, "Sole.Default", "alice");
        await SeedAsync(app, second, "Sole.Second", "alice");

        var blockers = await WithAsync(app, async p =>
            await p.GetRequiredService<AccountDeletionService>().GetBlockersAsync(
                await p.GetRequiredService<UserManager<BaGetUser>>().FindByNameAsync("alice"), Cancellation));

        Assert.Equal(
            new[] { ("default", "Sole.Default"), ("second", "Sole.Second") },
            blockers.LastOwnerPackages.Select(o => (o.RepositoryName, o.PackageId!)).OrderBy(o => o.Item1).ToArray());
    }

    // ---- Administrator deletes a user ----

    [Fact]
    public async Task AnAdministratorDeletingAUserCanDeleteSoleOwnedIdsInEveryRepository()
    {
        var app = Start();
        var second = await CreateSecondAsync(app);
        await CreateUserAsync(app, "root", administrator: true);
        await CreateUserAsync(app, "alice");
        await SeedAsync(app, RepositoryInfo.Default, "Sole.Default", "alice");
        await SeedAsync(app, second, "Sole.Second", "alice");
        var root = await SignedInAsync(app, "root");

        var html = await root.GetPageAsync("admin/users/alice/delete");
        Assert.Contains("Sole.Default", html);
        Assert.Contains("Sole.Second", html);
        Assert.Contains("second", html);

        using var response = await root.PostAsync("admin/users/alice/delete", new Dictionary<string, string>
        {
            { "password", Password },
            { "soleOwned", "delete" },
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using (var scope = Scope(app, RepositoryInfo.Default))
        {
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<IPackageDatabase>().FindAsync("Sole.Default", true, Cancellation));
        }

        using (var scope = Scope(app, second))
        {
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<IPackageDatabase>().FindAsync("Sole.Second", true, Cancellation));
        }
    }

    [Fact]
    public async Task KeepingSoleOwnedIdsLeavesThemInEveryRepositoryWithoutOwners()
    {
        var app = Start();
        var second = await CreateSecondAsync(app);
        await CreateUserAsync(app, "root", administrator: true);
        await CreateUserAsync(app, "alice");
        await SeedAsync(app, second, "Sole.Second", "alice");
        var root = await SignedInAsync(app, "root");

        using var response = await root.PostAsync("admin/users/alice/delete", new Dictionary<string, string>
        {
            { "password", Password },
            { "soleOwned", "keep" },
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = Scope(app, second);
        Assert.Single(await scope.ServiceProvider.GetRequiredService<IPackageDatabase>().FindAsync("Sole.Second", true, Cancellation));
    }

    // ---- Admin Packages ----

    [Fact]
    public async Task TheOwnerlessPageListsIdsOfEveryRepositoryWithTheirRepository()
    {
        var app = Start();
        var second = await CreateSecondAsync(app);
        await CreateUserAsync(app, "root", administrator: true);
        await SeedAsync(app, RepositoryInfo.Default, "Legacy.Default", owner: null);
        await SeedAsync(app, second, "Legacy.Second", owner: null);
        await SeedAsync(app, second, "Owned.Second", owner: "root");
        var root = await SignedInAsync(app, "root");

        var html = await root.GetPageAsync("admin/packages");

        Assert.Contains("/packages/Legacy.Default/manage", html);
        Assert.Contains("/r/second/packages/Legacy.Second/manage", html);
        Assert.DoesNotContain("Owned.Second", html);
    }

    [Fact]
    public async Task AnIdOwnedInOneRepositoryIsStillListedWhereItHasNoOwner()
    {
        var app = Start();
        var second = await CreateSecondAsync(app);
        await CreateUserAsync(app, "root", administrator: true);
        await SeedAsync(app, RepositoryInfo.Default, "Same.Id", owner: "root");
        await SeedAsync(app, second, "Same.Id", owner: null);
        var root = await SignedInAsync(app, "root");

        var html = await root.GetPageAsync("admin/packages");

        Assert.Contains("/r/second/packages/Same.Id/manage", html);
        Assert.DoesNotContain("href=\"/packages/Same.Id/manage\"", html);
    }
}
