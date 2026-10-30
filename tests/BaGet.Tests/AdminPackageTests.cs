using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Identity.Application.Services.Ownership;
using BaGet.Identity.Application.UseCases.Packages;

namespace BaGet.Tests;

/// <summary>
/// What an administrator may do to packages that are not theirs: the API and the Manage
/// page, hard deletion, and the lists that find a user's packages and the ownerless ones.
/// </summary>
public class AdminPackageTests : PackageManagementPageTestBase
{
    private const string Password = "Passw0rd!";

    public AdminPackageTests(ITestOutputHelper output)
        : base(output, deletionBehavior: null)
    {
    }

    private async Task<string> PromoteAsync(string userName)
        => await WithScopeAsync(async s =>
        {
            await s.GetRequiredService<AdministratorService>().PromoteAsync(userName, "test", CancellationToken.None);

            return userName;
        });

    private async Task<string> CreateKeyAsync(string userName, params string[]? scopes)
        => await WithScopeAsync(async s =>
        {
            var user = await s.GetRequiredService<UserManager<BaGetUser>>().FindByNameAsync(userName);

            #pragma warning disable CS8602
            return await s.GetRequiredService<ApiKeyService>().CreateAsync(
                user.Id, "test", scopes, "*", expires: null, CancellationToken.None);
            #pragma warning restore CS8602
        });

    private async Task<HttpStatusCode> DeleteAsync(string id, string version, string apiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/v2/package/{id}/{version}");
        request.Headers.Add("X-NuGet-ApiKey", apiKey);
        using var response = await Client.SendAsync(request);

        return response.StatusCode;
    }

    private Task<HttpResponseMessage> PostManageAsync(Browser browser, string id, string handler, Dictionary<string, string>? fields)
        => browser.PostAsync($"packages/{id}/manage", fields, action: $"packages/{id}/manage?handler={handler}");

    private async Task<HttpStatusCode> DownloadAsync(string id, string version)
    {
        using var response = await Client.GetAsync($"v3/package/{id.ToLowerInvariant()}/{version}/{id.ToLowerInvariant()}.{version}.nupkg");

        return response.StatusCode;
    }

    private async Task SeedOwnerlessAsync(string id, string version = "1.0.0")
    {
        using var package = TestPackages.Build(id, version);
        await App.AddPackageAsync(package);
    }

    private async Task<Browser> StartAdminAsync()
    {
        await Bob.RegisterAsync("root");
        await PromoteAsync("root");

        return Bob;
    }

    // --- API ----------------------------------------------------------------------------

    [Fact]
    public async Task AnAdministratorKeyUnlistsAnotherUsersPackage()
    {
        await Alice.RegisterAsync("alice");
        await Bob.RegisterAsync("root");
        await PromoteAsync("root");
        await PushAsAsync("alice", "Alice.Package");

        var plain = await CreateKeyAsync("root", ApiKeyScopes.Push);
        var unlist = await CreateKeyAsync("root", ApiKeyScopes.Unlist);

        // The key's own scopes still decide: a push-only key of an administrator is push-only.
        Assert.Equal(HttpStatusCode.Forbidden, await DeleteAsync("Alice.Package", "1.0.0", plain));
        Assert.Equal(HttpStatusCode.NoContent, await DeleteAsync("Alice.Package", "1.0.0", unlist));
        #pragma warning disable CS8602
        Assert.False((await FindAsync("Alice.Package", "1.0.0")).Listed);
        #pragma warning restore CS8602
    }

    [Fact]
    public async Task ANonAdministratorKeyStillCannotUnlistAnotherUsersPackage()
    {
        await Alice.RegisterAsync("alice");
        await Bob.RegisterAsync("bob");
        await PushAsAsync("alice", "Alice.Package");

        Assert.Equal(HttpStatusCode.Forbidden, await DeleteAsync("Alice.Package", "1.0.0", await CreateKeyAsync("bob", ApiKeyScopes.Unlist)));
    }

    // --- Manage page --------------------------------------------------------------------

    [Fact]
    public async Task AnAdministratorOpensManageForAnotherUsersPackageAndSeesTheBanner()
    {
        await Alice.RegisterAsync("alice");
        await PushAsAsync("alice", "Alice.Package");
        var admin = await StartAdminAsync();

        var html = await admin.GetPageAsync("packages/Alice.Package/manage");

        Assert.Contains("manage-as-administrator", html);
        Assert.Contains("Danger zone", html);
    }

    [Fact]
    public async Task AnOwnerWhoIsAlsoAnAdministratorSeesNoBannerAndTheDangerZone()
    {
        await Alice.RegisterAsync("alice");
        await PushAsAsync("alice", "Alice.Package");
        await PromoteAsync("alice");

        var html = await Alice.GetPageAsync("packages/Alice.Package/manage");

        Assert.DoesNotContain("manage-as-administrator", html);
        Assert.Contains("danger-zone", html);
    }

    [Fact]
    public async Task AnAdministratorPushingToAnExistingOwnerlessIdDoesNotClaimIt()
    {
        await SeedOwnerlessAsync("Legacy.Package");
        await Bob.RegisterAsync("root");
        await PromoteAsync("root");

        await PushAsAsync("root", "Legacy.Package", "2.0.0");

        Assert.NotNull(await FindAsync("Legacy.Package", "2.0.0"));
        Assert.Empty(await WithScopeAsync(s => s.GetRequiredService<PackageOwnershipService>().GetOwnersAsync("Legacy.Package", CancellationToken.None)));
    }

    [Fact]
    public async Task AnAdministratorPushingANewIdStillBecomesItsOwner()
    {
        await Bob.RegisterAsync("root");
        await PromoteAsync("root");

        await PushAsAsync("root", "Fresh.Package");

        Assert.Equal("root", Assert.Single(await WithScopeAsync(s => s.GetRequiredService<PackageOwnershipService>().GetOwnersAsync("Fresh.Package", CancellationToken.None))).OwnerName);
    }

    [Fact]
    public async Task AnAdministratorOpensManageForAnOwnerlessId()
    {
        await SeedOwnerlessAsync("Legacy.Package");
        var admin = await StartAdminAsync();

        var html = await admin.GetPageAsync("packages/Legacy.Package/manage");

        Assert.Contains("manage-as-administrator", html);
    }

    [Fact]
    public async Task ANonAdministratorIsForbiddenFromManagingAnotherUsersOrAnOwnerlessId()
    {
        await Alice.RegisterAsync("alice");
        await Bob.RegisterAsync("bob");
        await PushAsAsync("alice", "Alice.Package");
        await SeedOwnerlessAsync("Legacy.Package");

        using var other = await Bob.GetAsync("packages/Alice.Package/manage");
        using var ownerless = await Bob.GetAsync("packages/Legacy.Package/manage");

        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ownerless.StatusCode);
    }

    [Fact]
    public async Task AnOwnerWhoIsNotAnAdministratorHasNoHardDeleteControls()
    {
        await Alice.RegisterAsync("alice");
        await PushAsAsync("alice", "Alice.Package");

        var html = await Alice.GetPageAsync("packages/Alice.Package/manage");

        Assert.DoesNotContain("danger-zone", html);
        Assert.DoesNotContain("HardDelete", html);

        using var response = await PostManageAsync(Alice, "Alice.Package", "HardDelete", new Dictionary<string, string>
        {
            { "version", "1.0.0" },
            { "password", Password },
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotNull(await FindAsync("Alice.Package", "1.0.0"));
    }

    // --- Hard delete --------------------------------------------------------------------

    [Fact]
    public async Task AnAdministratorHardDeletesAVersionEvenWhenTheServerUnlists()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Alice.RegisterAsync("alice");
        await PushAsAsync("alice", "Alice.Package", "1.0.0");
        await PushAsAsync("alice", "Alice.Package", "2.0.0");
        var admin = await StartAdminAsync();

        // The per-version button shows because the server would only unlist.
        Assert.Contains("Delete permanently", await admin.GetPageAsync("packages/Alice.Package/manage"));

        using var response = await PostManageAsync(admin, "Alice.Package", "HardDelete", new Dictionary<string, string>
        {
            { "version", "1.0.0" },
            { "password", Password },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("deleted permanently", await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Null(await FindAsync("Alice.Package", "1.0.0"));
        Assert.Equal(HttpStatusCode.NotFound, await DownloadAsync("Alice.Package", "1.0.0"));

        // The other version is untouched.
        Assert.NotNull(await FindAsync("Alice.Package", "2.0.0"));
        Assert.Equal(HttpStatusCode.OK, await DownloadAsync("Alice.Package", "2.0.0"));
    }

    [Fact]
    public async Task AWrongPasswordDeletesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Alice.RegisterAsync("alice");
        await PushAsAsync("alice", "Alice.Package");
        var admin = await StartAdminAsync();

        using var one = await PostManageAsync(admin, "Alice.Package", "HardDelete", new Dictionary<string, string>
        {
            { "version", "1.0.0" },
            { "password", "wrong" },
        });
        using var all = await PostManageAsync(admin, "Alice.Package", "HardDeleteAll", new Dictionary<string, string>
        {
            { "password", "wrong" },
        });

        Assert.Contains("confirmation is incorrect", await one.Content.ReadAsStringAsync(cancellationToken));
        Assert.Contains("confirmation is incorrect", await all.Content.ReadAsStringAsync(cancellationToken));
        Assert.NotNull(await FindAsync("Alice.Package", "1.0.0"));
        Assert.Equal(HttpStatusCode.OK, await DownloadAsync("Alice.Package", "1.0.0"));
    }

    [Fact]
    public async Task HardDeletingEveryVersionRemovesTheOwnersToo()
    {
        await Alice.RegisterAsync("alice");
        await PushAsAsync("alice", "Alice.Package", "1.0.0");
        await PushAsAsync("alice", "Alice.Package", "2.0.0");
        var admin = await StartAdminAsync();

        using var response = await PostManageAsync(admin, "Alice.Package", "HardDeleteAll", new Dictionary<string, string>
        {
            { "password", Password },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await FindAsync("Alice.Package", "1.0.0"));
        Assert.Null(await FindAsync("Alice.Package", "2.0.0"));
        Assert.Equal(HttpStatusCode.NotFound, await DownloadAsync("Alice.Package", "2.0.0"));
        Assert.False(await WithScopeAsync(s => s.GetRequiredService<PackageOwnershipService>()
            .HasOwnersAsync("Alice.Package", CancellationToken.None)));
    }

    // --- Lists --------------------------------------------------------------------------

    [Fact]
    public async Task TheUserPackagesPageListsOnlyThatUsersIdsWithTheirOwners()
    {
        await Alice.RegisterAsync("alice");
        await PushAsAsync("alice", "Alice.Package");
        await SeedOwnerlessAsync("Legacy.Package");
        var admin = await StartAdminAsync();

        var html = await admin.GetPageAsync("admin/users/alice/packages");

        Assert.Contains("Alice.Package", html);
        Assert.Contains("/packages/Alice.Package/manage", html);
        Assert.DoesNotContain("Legacy.Package", html);

        using var unknown = await admin.GetAsync("admin/users/nobody/packages");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task TheOwnerlessPageListsOnlyIdsWithoutOwners()
    {
        await Alice.RegisterAsync("alice");
        await PushAsAsync("alice", "Alice.Package");
        await SeedOwnerlessAsync("Legacy.Package", "1.0.0");
        await SeedOwnerlessAsync("Legacy.Package", "2.0.0");
        var admin = await StartAdminAsync();

        var html = await admin.GetPageAsync("admin/packages");

        Assert.Contains("Legacy.Package", html);
        Assert.Contains("/packages/Legacy.Package/manage", html);
        Assert.Contains("2.0.0", html);
        Assert.DoesNotContain("Alice.Package", html);
    }

    [Theory]
    [InlineData("admin/packages")]
    [InlineData("admin/users/alice/packages")]
    public async Task TheAdministratorPackagePagesAreNotFoundForANonAdministrator(string url)
    {
        await Alice.RegisterAsync("alice");

        using var response = await Alice.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TheAdminMenuLinksToThePackagesPageOnlyForAnAdministrator()
    {
        await Alice.RegisterAsync("alice");
        var admin = await StartAdminAsync();

        Assert.Contains("/admin/packages", await admin.GetPageAsync("account/settings"));
        Assert.DoesNotContain("/admin/packages", await Alice.GetPageAsync("account/settings"));
    }
}

/// <summary>Read-only mode refuses administrators too.</summary>
public class AdminPackageReadOnlyTests : PackageManagementPageTestBase
{
    public AdminPackageReadOnlyTests(ITestOutputHelper output)
        : base(output, readOnly: true)
    {
    }

    [Fact]
    public async Task ReadOnlyModeRefusesAnAdministratorsHardDelete()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Alice.RegisterAsync("alice");
        await SeedOwnedPackageAsync("alice", "Alice.Package");
        await Bob.RegisterAsync("root");
        await WithScopeAsync(async s =>
        {
            await s.GetRequiredService<AdministratorService>().PromoteAsync("root", "test", CancellationToken.None);

            return true;
        });

        using var response = await Bob.PostAsync("packages/Alice.Package/manage", new Dictionary<string, string>
        {
            { "version", "1.0.0" },
            { "password", "Passw0rd!" },
        }, action: "packages/Alice.Package/manage?handler=HardDelete");

        Assert.Contains(PackageManageUseCase.ReadOnlyMessage, await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.NotNull(await FindAsync("Alice.Package", "1.0.0"));
    }
}

/// <summary>The audit trail of an administrator acting on packages that are not theirs.</summary>
public class AdminPackageAuditTests : IDisposable
{
    private readonly CapturingLoggerProvider _logs = new();
    private readonly BaGetApplication _app;

    public AdminPackageAuditTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string>
            {
                { "ApiKey", "global-key" },
                { "Accounts:Enabled", "true" },
            },
            configureServices: services => services.AddSingleton<ILoggerProvider>(_logs));
    }

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task AnAdministratorsWriteToAnotherUsersPackageIsLogged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var alice = new Browser(_app);
        using var admin = new Browser(_app);
        await alice.RegisterAsync("alice");
        await admin.RegisterAsync("root");

        using (var package = TestPackages.Build("Alice.Package", "1.0.0"))
        {
            await _app.AddPackageAsync(package, cancellationToken);
        }

        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PackageOwnershipService>()
                .AddOwnerAsync("Alice.Package", "alice", CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<AdministratorService>()
                .PromoteAsync("root", "test", CancellationToken.None);
        }

        using var response = await admin.PostAsync("packages/Alice.Package/manage", new Dictionary<string, string>
        {
            { "version", "1.0.0" },
        }, action: "packages/Alice.Package/manage?handler=Unlist");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Serilog renders string values quoted when it feeds an ILoggerProvider.
        Assert.Contains(_logs.Messages, m => m.Contains("Administrator \"root\" ran \"Unlist\" on \"Alice.Package\""));
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly ConcurrentQueue<string> _messages;

            public CapturingLogger(ConcurrentQueue<string> messages) => _messages = messages;

            #pragma warning disable CS8633
            public IDisposable? BeginScope<TState>(TState state) => null;
            #pragma warning restore CS8633

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => _messages.Enqueue(formatter(state, exception));
        }
    }
}
