using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;
using Xunit;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Tests;

/// <summary>
/// The pages an owner manages their own packages from: the "my packages" list and the
/// delete the <see cref="PackageDeletionBehavior"/> configures.
/// </summary>
public abstract class PackageManagementPageTestBase : IDisposable
{
    protected readonly BaGetApplication App;
    protected readonly HttpClient Client;
    // Browser is internal to the test assembly, so these cannot be protected.
    internal readonly Browser Alice;
    internal readonly Browser Bob;

    protected PackageManagementPageTestBase(
        ITestOutputHelper output,
        string? deletionBehavior = null,
        bool readOnly = false)
    {
        var overrides = new Dictionary<string, string>
        {
            { "ApiKey", "global-key" },
            { "Accounts:Enabled", "true" },
        };

        if (deletionBehavior != null)
        {
            overrides["PackageDeletionBehavior"] = deletionBehavior;
        }

        if (readOnly)
        {
            overrides["IsReadOnlyMode"] = "true";
        }

        App = new BaGetApplication(output, configOverrides: overrides);
        Client = App.CreateClient();
        Alice = new Browser(App);
        Bob = new Browser(App);
    }

    /// <summary>
    /// Push as the user would, so the package is really theirs: the first push of an ID
    /// is what makes its pusher an owner.
    /// </summary>
    protected async Task PushAsAsync(string userName, string packageId, string version = "1.0.0")
    {
        var key = await WithScopeAsync(async s =>
        {
            var user = await s.GetRequiredService<UserManager<BaGetUser>>().FindByNameAsync(userName);

            #pragma warning disable CS8602
            return await s.GetRequiredService<ApiKeyService>().CreateAsync(
                user.Id, "test", new[] { ApiKeyScopes.Push }, "*", expires: null, CancellationToken.None);
            #pragma warning restore CS8602
        });

        using var package = TestPackages.Build(packageId, version);
        using var request = TestPackages.PushRequest(package, key);
        using var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>
    /// Index a package and record its owner directly, for a server that would refuse the
    /// push itself.
    /// </summary>
    protected async Task SeedOwnedPackageAsync(string? userName, string packageId, string version = "1.0.0")
    {
        using (var package = TestPackages.Build(packageId, version))
        {
            await App.AddPackageAsync(package);
        }

        var result = await WithScopeAsync(s => s.GetRequiredService<PackageOwnershipService>()
            .AddOwnerAsync(packageId, userName, CancellationToken.None));

        Assert.Equal(OwnerChangeResult.Success, result);
    }

    protected Task<Package?> FindAsync(string? id, string version)
        => WithScopeAsync(s => s.GetRequiredService<IPackageDatabase>()
            .FindOrNullAsync(id, NuGetVersion.Parse(version), includeUnlisted: true, CancellationToken.None));

    protected async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = App.Services.CreateScope();

        return await action(scope.ServiceProvider);
    }

    public void Dispose()
    {
        Alice.Dispose();
        Bob.Dispose();
        Client.Dispose();
        App.Dispose();
    }
}

/// <summary>
/// The default server: deletion unlists.
/// </summary>
public class PackageManagementPageTests : PackageManagementPageTestBase
{
    public PackageManagementPageTests(ITestOutputHelper output)
        : base(output, deletionBehavior: null)
    {
    }

    [Fact]
    public async Task MyPackagesListsOnlyTheIdsTheUserOwns()
    {
        await Alice.RegisterAsync("alice");
        await Bob.RegisterAsync("bob");
        await PushAsAsync("alice", "Alice.Package");
        await PushAsAsync("bob", "Bob.Package");

        var html = await Alice.GetPageAsync("account/packages");

        Assert.Contains("Alice.Package", html);
        Assert.DoesNotContain("Bob.Package", html);
    }

    [Fact]
    public async Task MyPackagesShowsTheLatestVersionWithItsCountAndDownloads()
    {
        await Alice.RegisterAsync("alice");
        await PushAsAsync("alice", "Alice.Package", "1.0.0");
        await PushAsAsync("alice", "Alice.Package", "2.1.0");

        var html = await Alice.GetPageAsync("account/packages");

        // The highest version, not the first pushed, with both versions counted.
        Assert.Contains("2.1.0", html);
        Assert.Contains("<td>2</td>", html);
        // Nothing has been downloaded yet, and the count is the sum over the versions.
        Assert.Contains("<td>0</td>", html);
        // The management page for the ID, and a way to add another version to it.
        Assert.Contains("/packages/Alice.Package/manage", html);
        Assert.Contains("Upload new version", html);
    }

    [Fact]
    public async Task MyPackagesIsEmptyForAUserWhoOwnsNothing()
    {
        await Alice.RegisterAsync("alice");

        Assert.Contains("my-packages-empty", await Alice.GetPageAsync("account/packages"));
    }

    [Fact]
    public async Task MyPackagesNeedsASignedInUser()
    {
        using var response = await Bob.GetAsync("account/packages");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        #pragma warning disable CS8602
        Assert.Contains("/account/login", response.Headers.Location.ToString());
        #pragma warning restore CS8602
    }

    [Fact]
    public async Task TheDeleteButtonSaysUnlistWhenTheServerUnlists()
    {
        await Alice.RegisterAsync("alice");
        await PushAsAsync("alice", "Alice.Package");

        var html = await Alice.GetPageAsync("packages/Alice.Package/manage");

        Assert.Contains("Unlist this version", html);
        Assert.DoesNotContain("Delete this version permanently", html);
    }

    [Fact]
    public async Task DeletingUnlistsAndKeepsTheVersionRestorable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Alice.RegisterAsync("alice");
        await PushAsAsync("alice", "Alice.Package");

        using var response = await Alice.PostAsync("packages/Alice.Package/manage", new Dictionary<string, string>
        {
            { "version", "1.0.0" },
        }, action: "packages/Alice.Package/manage?handler=Delete");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("is unlisted", await response.Content.ReadAsStringAsync(cancellationToken));

        // The row and the file both stay: the version is undiscoverable, not gone.
        var package = await FindAsync("Alice.Package", "1.0.0");
        Assert.NotNull(package);
        Assert.False(package.Listed);

        using var download = await Client.GetAsync("v3/package/alice.package/1.0.0/alice.package.1.0.0.nupkg", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
    }

    [Fact]
    public async Task TheManagePageLinksToUploadingANewVersion()
    {
        await Alice.RegisterAsync("alice");
        await PushAsAsync("alice", "Alice.Package");

        var html = await Alice.GetPageAsync("packages/Alice.Package/manage");

        Assert.Contains("manage-upload", html);
        Assert.Contains("Upload new version", html);
        // The ID travels to the upload page, which reads it as a hint only.
        Assert.Contains("id=Alice.Package", html);
    }

    [Fact]
    public async Task MyPackagesListsIdsOwnedThroughAnOrganization()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Alice.RegisterAsync("alice");

        #pragma warning disable CS8602
        var aliceId = await WithScopeAsync(async s =>
            (await s.GetRequiredService<UserManager<BaGetUser>>().FindByNameAsync("alice")).Id);
        #pragma warning restore CS8602

        await WithScopeAsync(async s =>
        {
            var ownership = s.GetRequiredService<PackageOwnershipService>();

            Assert.True(await ownership.CreateOrganizationAsync("acme", aliceId, CancellationToken.None));

            return true;
        });

        using (var package = TestPackages.Build("Acme.Package", "1.0.0"))
        {
            await App.AddPackageAsync(package, cancellationToken);
        }

        Assert.Equal(
            OwnerChangeResult.Success,
            await WithScopeAsync(s => s.GetRequiredService<PackageOwnershipService>()
                .AddOwnerAsync("Acme.Package", "acme", CancellationToken.None)));

        var html = await Alice.GetPageAsync("account/packages");

        // The organization owns the ID, not Alice: the list unions her own name with
        // every organization she belongs to, which is the non-obvious half of the query.
        Assert.Contains("Acme.Package", html);
    }

    [Fact]
    public async Task UploadingANewVersionArrivesWithTheIdAsAHint()
    {
        await Alice.RegisterAsync("alice");

        var html = await Alice.GetPageAsync("Upload?id=Alice.Package");

        Assert.Contains("upload-for-id", html);
        Assert.Contains("Alice.Package", html);
    }
}

/// <summary>
/// A server configured to hard delete: deletion removes the rows and the files.
/// </summary>
public class PackageHardDeletePageTests : PackageManagementPageTestBase
{
    public PackageHardDeletePageTests(ITestOutputHelper output)
        : base(output, deletionBehavior: "HardDelete")
    {
    }

    [Fact]
    public async Task TheDeleteButtonSaysDeleteWhenTheServerHardDeletes()
    {
        await Alice.RegisterAsync("alice");
        await PushAsAsync("alice", "Alice.Package");

        var html = await Alice.GetPageAsync("packages/Alice.Package/manage");

        Assert.Contains("Delete this version permanently", html);
        Assert.DoesNotContain("Unlist this version", html);
    }

    [Fact]
    public async Task DeletingRemovesTheVersionAndItsFiles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Alice.RegisterAsync("alice");
        await PushAsAsync("alice", "Alice.Package");

        using var response = await Alice.PostAsync("packages/Alice.Package/manage", new Dictionary<string, string>
        {
            { "version", "1.0.0" },
        }, action: "packages/Alice.Package/manage?handler=Delete");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("is deleted", await response.Content.ReadAsStringAsync(cancellationToken));

        Assert.Null(await FindAsync("Alice.Package", "1.0.0"));

        using var download = await Client.GetAsync("v3/package/alice.package/1.0.0/alice.package.1.0.0.nupkg", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
    }

    [Fact]
    public async Task DeletingAVersionThatIsNotThereSaysSo()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Alice.RegisterAsync("alice");
        await PushAsAsync("alice", "Alice.Package");

        using var response = await Alice.PostAsync("packages/Alice.Package/manage", new Dictionary<string, string>
        {
            { "version", "9.9.9" },
        }, action: "packages/Alice.Package/manage?handler=Delete");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("was not in the database", await response.Content.ReadAsStringAsync(cancellationToken));

        // The version that is there is untouched.
        Assert.NotNull(await FindAsync("Alice.Package", "1.0.0"));
    }
}

/// <summary>
/// A read-only server refuses a deletion, as the DELETE endpoint does.
/// </summary>
public class PackageDeleteReadOnlyTests : PackageManagementPageTestBase
{
    public PackageDeleteReadOnlyTests(ITestOutputHelper output)
        : base(output, deletionBehavior: "HardDelete", readOnly: true)
    {
    }

    [Fact]
    public async Task AReadOnlyServerRefusesToDelete()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Alice.RegisterAsync("alice");

        // Seeded rather than pushed: a read-only server refuses the push too.
        await SeedOwnedPackageAsync("alice", "Alice.Package");

        using var response = await Alice.PostAsync("packages/Alice.Package/manage", new Dictionary<string, string>
        {
            { "version", "1.0.0" },
        }, action: "packages/Alice.Package/manage?handler=Delete");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("read-only mode", await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.NotNull(await FindAsync("Alice.Package", "1.0.0"));
    }

    [Fact]
    public async Task AReadOnlyServerRefusesToUnlist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Alice.RegisterAsync("alice");
        await SeedOwnedPackageAsync("alice", "Alice.Package");

        using var response = await PostAsync("Unlist", new Dictionary<string, string>
        {
            { "version", "1.0.0" },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("read-only mode", await response.Content.ReadAsStringAsync(cancellationToken));

        // Unlisting is the very operation the Delete button is said to perform, so a
        // server that refuses one must refuse the other.
        #pragma warning disable CS8602
        Assert.True((await FindAsync("Alice.Package", "1.0.0")).Listed);
        #pragma warning restore CS8602
    }

    [Fact]
    public async Task AReadOnlyServerRefusesToRelist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Alice.RegisterAsync("alice");
        await SeedOwnedPackageAsync("alice", "Alice.Package");

        await WithScopeAsync(s => s.GetRequiredService<IPackageDatabase>()
            .UnlistPackageAsync("Alice.Package", NuGetVersion.Parse("1.0.0"), CancellationToken.None));

        using var response = await PostAsync("Relist", new Dictionary<string, string>
        {
            { "version", "1.0.0" },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("read-only mode", await response.Content.ReadAsStringAsync(cancellationToken));
        #pragma warning disable CS8602
        Assert.False((await FindAsync("Alice.Package", "1.0.0")).Listed);
        #pragma warning restore CS8602
    }

    [Fact]
    public async Task AReadOnlyServerRefusesToDeprecate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Alice.RegisterAsync("alice");
        await SeedOwnedPackageAsync("alice", "Alice.Package");

        using var response = await PostAsync("Deprecate", new Dictionary<string, string>
        {
            { "version", "1.0.0" },
            { "reasons", PackageDeprecation.KnownReasons[0] },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("read-only mode", await response.Content.ReadAsStringAsync(cancellationToken));
        #pragma warning disable CS8602
        Assert.Null((await FindAsync("Alice.Package", "1.0.0")).Deprecation);
        #pragma warning restore CS8602
    }

    [Fact]
    public async Task AReadOnlyServerRefusesToChangeTheOwners()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Alice.RegisterAsync("alice");
        await Bob.RegisterAsync("bob");
        await SeedOwnedPackageAsync("alice", "Alice.Package");

        using var response = await PostAsync("AddOwner", new Dictionary<string, string>
        {
            { "ownerName", "bob" },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("read-only mode", await response.Content.ReadAsStringAsync(cancellationToken));

        var owners = await WithScopeAsync(s => s.GetRequiredService<PackageOwnershipService>()
            .GetOwnersAsync("Alice.Package", CancellationToken.None));

        Assert.Equal("alice", Assert.Single(owners).OwnerName);
    }

    [Fact]
    public async Task AReadOnlyManagePageSaysSo()
    {
        await Alice.RegisterAsync("alice");
        await SeedOwnedPackageAsync("alice", "Alice.Package");

        var html = await Alice.GetPageAsync("packages/Alice.Package/manage");

        Assert.Contains("manage-read-only", html);
    }

    private Task<HttpResponseMessage> PostAsync(string handler, Dictionary<string, string>? fields)
        => Alice.PostAsync(
            "packages/Alice.Package/manage",
            fields,
            action: $"packages/Alice.Package/manage?handler={handler}");
}
