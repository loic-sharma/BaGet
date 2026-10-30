using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;

namespace BaGet.Tests;

/// <summary>
/// B1 and U1: every repository page is reachable at <c>/</c> (default) and <c>/r/{repo}/</c>, links stay in the
/// repository being browsed, the header switcher moves between repositories, and a browser upload goes to the
/// repository of its address.
/// </summary>
public class RepositoryPagesTests : IDisposable
{
    private const string Second = "second";
    private const string ApiKey = "pages-key";

    private readonly ITestOutputHelper _output;
    private readonly BaGetApplication _app;
    private readonly Browser _browser;

    public RepositoryPagesTests(ITestOutputHelper output)
    {
        _output = output;
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string> { { "ApiKey", ApiKey } });
        _browser = new Browser(_app);
        _app.CreateRepositoryAsync(Second).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _browser.Dispose();
        _app.Dispose();
    }

    private async Task AddAsync(string id, string version, string? repository)
    {
        using var package = TestPackages.Build(id, version);
        await _app.AddPackageAsync(package, CancellationToken.None, repository);
    }

    [Theory]
    [InlineData("")]
    [InlineData("packages")]
    [InlineData("stats")]
    [InlineData("upload")]
    [InlineData("packages/Pages.Lib")]
    [InlineData("packages/Pages.Lib/1.0.0")]
    [InlineData("packages/Pages.Lib/1.0.0/report-abuse")]
    public async Task EveryRepositoryPageIsServedAtBothAddresses(string path)
    {
        using var plain = await _browser.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);

        using var prefixed = await _browser.GetAsync(("r/second/" + path).TrimEnd('/'));
        Assert.Equal(HttpStatusCode.OK, prefixed.StatusCode);
    }

    [Fact]
    public async Task AnUnknownRepositoryIsNotFoundForPagesToo()
    {
        foreach (var path in new[] { "r/nope", "r/nope/packages", "r/nope/stats", "r/nope/upload" })
        {
            using var response = await _browser.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task LinksOfAPrefixedPageStayInTheRepository()
    {
        var html = await _browser.GetPageAsync("r/second/packages");

        Assert.Contains("href=\"/r/second/upload\"", html);
        Assert.Contains("href=\"/r/second/stats\"", html);
        Assert.Contains("href=\"/r/second/packages\"", html);
        Assert.Contains("href=\"/r/second/\"", html); // the brand
        Assert.DoesNotContain("href=\"/upload\"", html);
        Assert.DoesNotContain("href=\"/stats\"", html);
    }

    [Fact]
    public async Task LinksOfAPlainPageAreUnchanged()
    {
        foreach (var path in new[] { "packages", "stats", "" })
        {
            var html = await _browser.GetPageAsync(path);

            Assert.Contains("href=\"/upload\"", html);
            Assert.Contains("href=\"/stats\"", html);
            Assert.Contains("href=\"/packages\"", html);
            Assert.DoesNotContain("/r/second/upload", html);
            Assert.DoesNotContain("/r/second/stats", html);
            Assert.DoesNotContain("/r/second/packages", html);
        }
    }

    [Fact]
    public async Task ThePackageListAndPackagePageShowTheRepositoryOfTheirAddress()
    {
        await AddAsync("Only.In.Second", "1.0.0", Second);
        await AddAsync("Only.In.Default", "1.0.0", null);

        var secondList = await _browser.GetPageAsync("r/second/packages?q=Only");
        Assert.Contains("Only.In.Second", secondList);
        Assert.DoesNotContain("Only.In.Default", secondList);
        // The result links to the package page of the same repository.
        Assert.Contains("href=\"/r/second/packages/only.in.second", secondList, StringComparison.OrdinalIgnoreCase);

        var defaultList = await _browser.GetPageAsync("packages?q=Only");
        Assert.Contains("Only.In.Default", defaultList);
        Assert.DoesNotContain("Only.In.Second", defaultList);
        Assert.DoesNotContain("href=\"/r/second/packages/", defaultList);

        Assert.Contains("Only.In.Second", await _browser.GetPageAsync("r/second/packages/Only.In.Second"));
        Assert.Contains("Could not find package", await _browser.GetPageAsync("packages/Only.In.Second"));
        Assert.Contains("Could not find package", await _browser.GetPageAsync("r/second/packages/Only.In.Default"));
    }

    [Fact]
    public async Task ThePackagePageOfASecondRepositoryUsesItsOwnFeedForDownloads()
    {
        await AddAsync("Feed.Check", "1.0.0", Second);

        var html = await _browser.GetPageAsync("r/second/packages/Feed.Check");

        Assert.Contains("/r/second/v3/package/feed.check/1.0.0/feed.check.1.0.0.nupkg", html);
    }

    [Fact]
    public async Task TheFrontPageOfARepositoryIsItsRoot()
    {
        // Root = the home hero, search without a query = the list.
        Assert.Contains("home-hero", await _browser.GetPageAsync("r/second"));
        Assert.Contains("home-hero", await _browser.GetPageAsync("r/second/"));
        Assert.DoesNotContain("home-hero", await _browser.GetPageAsync("r/second/packages"));
        Assert.DoesNotContain("home-hero", await _browser.GetPageAsync("r/second?q="));
    }

    [Fact]
    public async Task TheTitleNamesANonDefaultRepository()
    {
        Assert.Contains("(second)", await _browser.GetPageAsync("r/second/stats"));
        Assert.DoesNotContain("(second)", await _browser.GetPageAsync("stats"));
    }

    [Fact]
    public async Task TheSwitcherListsRepositoriesAndMarksTheCurrentOne()
    {
        var html = await _browser.GetPageAsync("r/second/stats");

        Assert.Contains("id=\"repository-switcher\"", html);
        Assert.Contains("href=\"/\" data-repository=\"default\"", html);
        Assert.Contains("href=\"/r/second/\" data-repository=\"second\"", html);
        Assert.Contains("id=\"current-repository\">second<", html);

        Assert.Contains("id=\"current-repository\">default<", await _browser.GetPageAsync("stats"));
    }

    [Fact]
    public async Task TheSwitcherIsAbsentWithOnlyTheDefaultRepository()
    {
        using var app = new BaGetApplication(_output, configOverrides: new Dictionary<string, string> { { "ApiKey", ApiKey } });
        using var browser = new Browser(app);

        Assert.DoesNotContain("repository-switcher", await browser.GetPageAsync("stats"));
    }

    [Fact]
    public async Task TheSwitcherIsAbsentOnPagesThatDoNotActOnARepository()
    {
        // Account pages belong to the whole server.
        using var app = new BaGetApplication(_output, configOverrides: new Dictionary<string, string>
        {
            { "Accounts:Enabled", "true" },
            { "ApiKey", ApiKey },
        });
        await app.CreateRepositoryAsync(Second, TestContext.Current.CancellationToken);
        using var browser = new Browser(app);

        Assert.DoesNotContain("repository-switcher", await browser.GetPageAsync("account/login"));
        Assert.Contains("repository-switcher", await browser.GetPageAsync("stats"));
    }

    [Fact]
    public async Task TheUploadPagePicksTheRepositoryByItsAddress()
    {
        var plain = await _browser.GetPageAsync("upload");
        Assert.Contains("id=\"upload-repository\"", plain);
        Assert.Contains("class=\"upload-repository-current\">default<", plain);
        // The commands follow the repository.
        Assert.Contains("dotnet nuget push -s http://localhost/v3/index.json", plain);
        Assert.Contains("href=\"/r/second/upload\" data-repository=\"second\"", plain);

        var second = await _browser.GetPageAsync("r/second/upload");
        Assert.Contains("dotnet nuget push -s http://localhost/r/second/v3/index.json", second);
        Assert.Contains("-SourceLocation \\\"http://localhost/r/second/api/v2\\\"", second);
        // Leaving for the default repository drops the prefix even though the request carries one.
        Assert.Contains("href=\"/upload\" data-repository=\"default\"", second);
        Assert.DoesNotContain("href=\"/r/second/upload\" data-repository", second);
    }

    [Fact]
    public async Task AnUploadGoesToTheRepositoryOfItsAddress()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var stream = TestPackages.Build("Browser.Second", "1.0.0");

        using var staged = await _browser.PostFileAsync(
            "r/second/upload?handler=Stage",
            "package",
            "Browser.Second.1.0.0.nupkg",
            stream,
            new Dictionary<string, string> { { "apiKey", ApiKey } },
            "r/second/upload");
        var preview = await staged.Content.ReadAsStringAsync(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, staged.StatusCode);
        var token = BrowserUpload.StagedToken(preview);

        // The submit form posts back to the same repository's address.
        Assert.Contains("action=\"/r/second/upload?handler=Submit\"", preview);

        using var submitted = await _browser.PostAsync(
            "r/second/upload",
            new Dictionary<string, string> { { "token", token }, { "acceptTerms", "true" }, { "apiKey", ApiKey } },
            "r/second/upload?handler=Submit");
        var html = await submitted.Content.ReadAsStringAsync(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        Assert.Contains("Browser.Second 1.0.0 was published", html);
        // "View it" links to the package page of the same repository.
        Assert.Contains("href=\"/r/second/packages/browser.second/1.0.0\"", html, StringComparison.OrdinalIgnoreCase);

        using var inSecond = await _browser.GetAsync("r/second/v3/package/browser.second/index.json");
        Assert.Equal(HttpStatusCode.OK, inSecond.StatusCode);
        using var inDefault = await _browser.GetAsync("v3/package/browser.second/index.json");
        Assert.Equal(HttpStatusCode.NotFound, inDefault.StatusCode);
    }

    [Fact]
    public async Task AReadAccessRefusalIsNotFoundAndHidesTheRepositoryFromTheSwitcher()
    {
        using var app = new BaGetApplication(
            _output,
            configOverrides: new Dictionary<string, string> { { "ApiKey", ApiKey } },
            configureServices: services => services.AddScoped<IRepositoryAccess, DenySecond>());
        await app.CreateRepositoryAsync(Second, TestContext.Current.CancellationToken);
        using var browser = new Browser(app);

        foreach (var path in new[] { "r/second", "r/second/packages", "r/second/stats", "r/second/upload" })
        {
            using var response = await browser.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        var html = await browser.GetPageAsync("stats");
        Assert.DoesNotContain("repository-switcher", html);
        Assert.DoesNotContain("/r/second", html);
    }

    private sealed class DenySecond : IRepositoryAccess
    {
        public bool SupportsMultipleRepositories => true;

        public Task<IReadOnlyList<RepositoryInfo>> ReadableAsync(ClaimsPrincipal? user, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RepositoryInfo>>(new[] { RepositoryInfo.Default });

        public Task<IReadOnlyList<RepositoryInfo>> WritableAsync(ClaimsPrincipal? user, CancellationToken cancellationToken)
            => ReadableAsync(user, cancellationToken);

        public Task<bool> CanReadAsync(ClaimsPrincipal? user, RepositoryInfo repository, CancellationToken cancellationToken)
            => Task.FromResult(repository.IsDefault);
    }
}

/// <summary>
/// The account-dependent U1 pages: My packages grouped by repository, and the administrator's Repositories list with
/// its Browse and Upload links.
/// </summary>
public class RepositoryAccountPagesTests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly Browser _admin;

    public RepositoryAccountPagesTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "Accounts:Enabled", "true" },
            { "ApiKey", "" },
        });
        _admin = new Browser(_app);
        CreateUserAsync(_app, "root", administrator: true).GetAwaiter().GetResult();
        SignInAsync(_admin, "root").GetAwaiter().GetResult();
        _app.CreateRepositoryAsync("second").GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _admin.Dispose();
        _app.Dispose();
    }

    [Fact]
    public async Task TheRepositoriesListLinksToEachRepositorysPagesAndUpload()
    {
        var html = await _admin.GetPageAsync("admin/repositories");

        Assert.Contains("href=\"/upload\"", html);
        Assert.Contains("href=\"/r/second/upload\"", html);
        Assert.Contains("class=\"repository-browse\" href=\"/r/second", html);
        Assert.Matches("class=\"repository-browse\" href=\"/(packages)?\"", html);
    }

    [Fact]
    public async Task MyPackagesGroupsTheOwnedIdsByRepository()
    {
        await AddOwnedAsync("root", "Mine.Default", null);
        await AddOwnedAsync("root", "Mine.Second", "second");

        var html = await _admin.GetPageAsync("account/packages");

        Assert.Contains("data-repository=\"default\"", html);
        Assert.Contains("data-repository=\"second\"", html);
        Assert.Contains("Repository: second", html);
        Assert.Contains("href=\"/packages/Mine.Default\"", html);
        Assert.Contains("href=\"/r/second/packages/Mine.Second\"", html);
        Assert.Contains("href=\"/r/second/packages/Mine.Second/manage\"", html);
        Assert.Contains("href=\"/r/second/upload?id=Mine.Second\"", html);
        Assert.DoesNotContain("/r/second/packages/Mine.Default", html);
    }

    private async Task AddOwnedAsync(string owner, string id, string? repository)
    {
        using var package = TestPackages.Build(id, "1.0.0");
        await _app.AddPackageAsync(package, CancellationToken.None, repository);

        using var scope = await _app.CreateRepositoryScopeAsync(repository);
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
#pragma warning disable CS8602
        context.PackageOwners.Add(new PackageOwner { PackageId = id, OwnerName = owner });
#pragma warning restore CS8602
        await context.SaveChangesAsync(CancellationToken.None);
    }
}
