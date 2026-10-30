using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// Statistics and abuse reports, which work with or without accounts.
/// </summary>
public class WebUiIntegrationTests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly Browser _browser;

    public WebUiIntegrationTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output);
        _browser = new Browser(_app);
    }

    [Fact]
    public async Task StatisticsCountThePackages()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using (var package = TestResources.GetResourceStream(TestResources.Package))
        {
            await _app.AddPackageAsync(package, cancellationToken);
        }

        var html = await _browser.GetPageAsync("stats");

        Assert.Contains("<h2 id=\"stat-ids\">1</h2>", html);
        Assert.Contains("<h2 id=\"stat-versions\">1</h2>", html);
        Assert.Contains("TestData", html);
    }

    [Fact]
    public async Task AnAbuseReportIsRecorded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _browser.PostAsync("packages/TestData/1.2.3/report-abuse", new Dictionary<string, string>
        {
            { "Reason", "Contains malicious code" },
            { "Details", "It mines coins." },
            { "ReporterEmail", "reporter@example.test" },
        });

        Assert.Contains("report-submitted", await response.Content.ReadAsStringAsync(cancellationToken));

        var report = Assert.Single(await ReportsAsync());
        Assert.Equal("TestData", report.PackageId);
        Assert.Equal("1.2.3", report.PackageVersion);
        Assert.Equal("It mines coins.", report.Details);
        Assert.Null(report.Resolved);
    }

    [Fact]
    public async Task AReportNeedsAKnownReason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _browser.PostAsync("packages/TestData/1.2.3/report-abuse", new Dictionary<string, string>
        {
            { "Reason", "Because" },
            { "Details", "x" },
        });

        Assert.DoesNotContain("report-submitted", await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(await ReportsAsync());
    }

    private async Task<List<AbuseReport>> ReportsAsync()
    {
        using var scope = _app.Services.CreateScope();

        #pragma warning disable CS8604
        return await scope.ServiceProvider.GetRequiredService<IContext>().AbuseReports.ToListAsync();
        #pragma warning restore CS8604
    }

    public void Dispose()
    {
        _browser.Dispose();
        _app.Dispose();
    }
}

/// <summary>
/// The owner-facing pages: profiles, package management and organizations.
/// </summary>
public class AccountsUiIntegrationTests : IDisposable
{
    private const string GlobalKey = "global-key";

    private readonly BaGetApplication _app;
    private readonly HttpClient _client;
    private readonly Browser _alice;
    private readonly Browser _bob;

    public AccountsUiIntegrationTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "ApiKey", GlobalKey },
            { "Accounts:Enabled", "true" },
        });
        _client = _app.CreateClient();
        _alice = new Browser(_app);
        _bob = new Browser(_app);
    }

    [Fact]
    public async Task AProfileListsTheUsersPackages()
    {
        await _alice.RegisterAsync("alice");
        await PushAsAsync("alice", "Alice.Package");

        using var redirect = await _alice.GetAsync("account/profile");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        #pragma warning disable CS8602
        Assert.EndsWith("/profiles/alice", redirect.Headers.Location.ToString());
        #pragma warning restore CS8602

        var html = await _bob.GetPageAsync("profiles/alice");
        Assert.Contains("Alice.Package", html);
    }

    [Fact]
    public async Task AnUnknownProfileIsNotFound()
    {
        using var response = await _bob.GetAsync("profiles/nobody");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnOwnerCanUnlistFromTheManagePage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _alice.RegisterAsync("alice");
        await PushAsAsync("alice", "Alice.Package");

        using var response = await _alice.PostAsync("packages/Alice.Package/manage", new Dictionary<string, string>
        {
            { "version", "1.0.0" },
        }, action: "packages/Alice.Package/manage?handler=Unlist");

        Assert.Contains("is unlisted", await response.Content.ReadAsStringAsync(cancellationToken));
        var package = await WithScopeAsync(s => s.GetRequiredService<IPackageDatabase>()
            .FindOrNullAsync("Alice.Package", NuGet.Versioning.NuGetVersion.Parse("1.0.0"), includeUnlisted: true, CancellationToken.None));
        #pragma warning disable CS8602
        Assert.False(package.Listed);
        #pragma warning restore CS8602
    }

    [Fact]
    public async Task AnOwnerCanDeprecateFromTheManagePage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _alice.RegisterAsync("alice");
        await PushAsAsync("alice", "Alice.Package");

        using var response = await _alice.PostAsync("packages/Alice.Package/manage", new Dictionary<string, string>
        {
            { "version", "1.0.0" },
            { "reasons", "Legacy" },
            { "message", "Use the new one" },
        }, action: "packages/Alice.Package/manage?handler=Deprecate");

        Assert.Contains("is deprecated", await response.Content.ReadAsStringAsync(cancellationToken));
        var registration = await _client.GetStringAsync("v3/registration/alice.package/index.json", cancellationToken);
        Assert.Contains("Use the new one", registration);
    }

    [Fact]
    public async Task SomeoneElseCannotManageThePackage()
    {
        await _alice.RegisterAsync("alice");
        await _bob.RegisterAsync("bob");
        await PushAsAsync("alice", "Alice.Package");

        using var response = await _bob.GetAsync("packages/Alice.Package/manage");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TheManagePageNeedsASignedInUser()
    {
        using var response = await _bob.GetAsync("packages/Alice.Package/manage");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        #pragma warning disable CS8602
        Assert.Contains("/account/login", response.Headers.Location.ToString());
        #pragma warning restore CS8602
    }

    [Fact]
    public async Task AnOwnerCanAddAnotherOwner()
    {
        await _alice.RegisterAsync("alice");
        await _bob.RegisterAsync("bob");
        await PushAsAsync("alice", "Alice.Package");

        using (await _alice.PostAsync("packages/Alice.Package/manage", new Dictionary<string, string>
               {
                   { "ownerName", "bob" },
               }, action: "packages/Alice.Package/manage?handler=AddOwner"))
        {
        }

        using var response = await _bob.GetAsync("packages/Alice.Package/manage");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OwnersSeeAndResolveAbuseReports()
    {
        await _alice.RegisterAsync("alice");
        await PushAsAsync("alice", "Alice.Package");
        using (await _bob.PostAsync("packages/Alice.Package/1.0.0/report-abuse", new Dictionary<string, string>
               {
                   { "Reason", "Other" },
                   { "Details", "Suspicious behaviour" },
               }))
        {
        }

        var html = await _alice.GetPageAsync("packages/Alice.Package/manage");
        Assert.Contains("Suspicious behaviour", html);

        #pragma warning disable CS8604
        var key = await WithScopeAsync(async s => (await s.GetRequiredService<IContext>().AbuseReports.SingleAsync()).Key);
        #pragma warning restore CS8604
        using (await _alice.PostAsync("packages/Alice.Package/manage", new Dictionary<string, string>
               {
                   { "key", key.ToString() },
               }, action: "packages/Alice.Package/manage?handler=ResolveReport"))
        {
        }

        Assert.DoesNotContain("Suspicious behaviour", await _alice.GetPageAsync("packages/Alice.Package/manage"));
    }

    [Fact]
    public async Task AUserCanCreateAnOrganization()
    {
        await _alice.RegisterAsync("alice");

        using (await _alice.PostAsync("account/organizations", new Dictionary<string, string>
               {
                   { "name", "contoso" },
               }, action: "account/organizations?handler=Create"))
        {
        }

        var html = await _bob.GetPageAsync("profiles/contoso");
        Assert.Contains("alice", html);
    }

    private async Task PushAsAsync(string userName, string packageId)
    {
        var key = await WithScopeAsync(async s =>
        {
            var user = await s.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<BaGetUser>>().FindByNameAsync(userName);

            #pragma warning disable CS8602
            return await s.GetRequiredService<ApiKeyService>().CreateAsync(
                user.Id, "test", new[] { ApiKeyScopes.Push }, "*", expires: null, CancellationToken.None);
            #pragma warning restore CS8602
        });

        using var package = TestPackages.Build(packageId, "1.0.0");
        using var request = TestPackages.PushRequest(package, key);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = _app.Services.CreateScope();

        return await action(scope.ServiceProvider);
    }

    public void Dispose()
    {
        _alice.Dispose();
        _bob.Dispose();
        _client.Dispose();
        _app.Dispose();
    }
}