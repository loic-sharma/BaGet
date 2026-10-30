using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Services.Ownership;
using BaGet.Identity.Application.UseCases.Packages;

namespace BaGet.Tests;

/// <summary>
/// The package-management web screens (Packages/Manage, ContactOwners, ValidationStatus, Scan, ...), which call their JSON twin in process. The twins
/// themselves are screen-only (403 over HTTP to everybody, see <c>UiApiAccountTests</c>), so the screens are driven here the way a browser does: the gates,
/// the statuses, and a write through the page gives the state it always did.
/// </summary>
public class UiApiPackageScreensTests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly Browser _anonymous;
    private readonly Browser _alice;
    private readonly Browser _bob;
    private readonly Browser _root;

    public UiApiPackageScreensTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "true" } });
        _anonymous = new Browser(_app);
        _alice = new Browser(_app);
        _bob = new Browser(_app);
        _root = new Browser(_app);
        SetUpAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _anonymous.Dispose();
        _alice.Dispose();
        _bob.Dispose();
        _root.Dispose();
        _app.Dispose();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task SetUpAsync()
    {
        await CreateUserAsync(_app, "alice");
        await CreateUserAsync(_app, "bob");
        await CreateUserAsync(_app, "root", administrator: true);
        await SignInAsync(_alice, "alice");
        await SignInAsync(_bob, "bob");
        await SignInAsync(_root, "root");
    }

    private static async Task<HttpStatusCode> StatusAsync(Browser browser, string path)
    {
        using var response = await browser.GetAsync(path);

        return response.StatusCode;
    }

    private async Task<Package?> PackageAsync(string id, string version)
    {
        using var scope = _app.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IPackageDatabase>().FindOrNullAsync(id, NuGetVersion.Parse(version), includeUnlisted: true, Token);
    }

    [Fact]
    public async Task TheManageScreenShowsTheModelOfItsTwin()
    {
        await PushAsync(_app, "alice", "Alice.Package");

        var page = await _alice.GetPageAsync("packages/Alice.Package/manage");

        Assert.Contains("Manage Alice.Package", page);
        Assert.Contains(">1.0.0</a>", page);
        Assert.Contains(">alice</a>", page);
    }

    [Fact]
    public async Task TheManageStatusesAreThoseOfTheGate()
    {
        await PushAsync(_app, "alice", "Alice.Package");

        Assert.Equal(HttpStatusCode.Redirect, await StatusAsync(_anonymous, "packages/Alice.Package/manage"));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(_bob, "packages/Alice.Package/manage"));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(_root, "packages/Alice.Package/manage"));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(_root, "packages/Nobody.Knows/manage"));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(_alice, "r/nope/packages/Alice.Package/manage"));
    }

    [Fact]
    public async Task AnUnlistAndARelistThroughThePageChangeTheState()
    {
        await PushAsync(_app, "alice", "Alice.Package");

        using var unlist = await _alice.PostAsync(
            "packages/Alice.Package/manage",
            new Dictionary<string, string> { { "version", "1.0.0" } },
            action: "packages/Alice.Package/manage?handler=Unlist");
        Assert.Contains("is unlisted", await unlist.Content.ReadAsStringAsync(Token));
        Assert.False((await PackageAsync("Alice.Package", "1.0.0"))!.Listed);

        using var relist = await _alice.PostAsync(
            "packages/Alice.Package/manage",
            new Dictionary<string, string> { { "version", "1.0.0" } },
            action: "packages/Alice.Package/manage?handler=Relist");
        Assert.Contains("is relisted", await relist.Content.ReadAsStringAsync(Token));
        Assert.True((await PackageAsync("Alice.Package", "1.0.0"))!.Listed);
    }

    [Fact]
    public async Task AnOwnerIsAddedAndRemovedThroughThePage()
    {
        await PushAsync(_app, "alice", "Alice.Package");

        using var added = await _alice.PostAsync(
            "packages/Alice.Package/manage",
            new Dictionary<string, string> { { "ownerName", "bob" } },
            action: "packages/Alice.Package/manage?handler=AddOwner");
        Assert.Contains("bob is now an owner.", await added.Content.ReadAsStringAsync(Token));

        using var removed = await _alice.PostAsync(
            "packages/Alice.Package/manage",
            new Dictionary<string, string> { { "ownerName", "bob" } },
            action: "packages/Alice.Package/manage?handler=RemoveOwner");
        Assert.Contains("bob is no longer an owner.", await removed.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task ContactOwnersIs404WhileNoMailIsConfigured()
    {
        await PushAsync(_app, "alice", "Alice.Package");

        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(_bob, "packages/Alice.Package/contact-owners"));
    }

    [Fact]
    public async Task TheValidationStatusPageSendsAnAnonymousVisitorToSignIn()
    {
        await PushAsync(_app, "alice", "Alice.Package");

        Assert.Equal(HttpStatusCode.Redirect, await StatusAsync(_anonymous, "packages/Alice.Package/validation"));
    }
}

/// <summary>The package screen on a server that refuses writes (<c>IsReadOnlyMode</c>): the page shows the message and changes nothing.</summary>
public class UiApiPackageScreensReadOnlyTests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly Browser _alice;

    public UiApiPackageScreensReadOnlyTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "true" }, { "IsReadOnlyMode", "true" } });
        _alice = new Browser(_app);
    }

    public void Dispose()
    {
        _alice.Dispose();
        _app.Dispose();
    }

    [Fact]
    public async Task AWriteOnAReadOnlyServerShowsTheMessageOnThePageAndChangesNothing()
    {
        var token = TestContext.Current.CancellationToken;
        await CreateUserAsync(_app, "alice");
        await SignInAsync(_alice, "alice");
        using (var package = TestPackages.Build("Alice.Package", "1.0.0"))
        {
            await _app.AddPackageAsync(package, token);
        }

        using (var scope = _app.Services.CreateScope())
        {
            Assert.Equal(OwnerChangeResult.Success, await scope.ServiceProvider.GetRequiredService<PackageOwnershipService>().AddOwnerAsync("Alice.Package", "alice", token));
        }

        using var page = await _alice.PostAsync(
            "packages/Alice.Package/manage",
            new Dictionary<string, string> { { "version", "1.0.0" } },
            action: "packages/Alice.Package/manage?handler=Unlist");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains(PackageManageUseCase.ReadOnlyMessage, await page.Content.ReadAsStringAsync(token));

        using var scope2 = _app.Services.CreateScope();
        Assert.True((await scope2.ServiceProvider.GetRequiredService<IPackageDatabase>().FindOrNullAsync("Alice.Package", NuGetVersion.Parse("1.0.0"), includeUnlisted: true, token))!.Listed);
    }
}
