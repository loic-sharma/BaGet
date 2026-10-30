using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Protocol.Models;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// The search page's sort control, and the guarantee that the V3 search API is untouched by it.
/// </summary>
public class SearchSortWebTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public SearchSortWebTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }

    private (BaGetApplication App, Browser Browser) Start(
        Dictionary<string, string>? settings = null,
        Action<IServiceCollection>? configure = null)
    {
        var app = new BaGetApplication(_output, configOverrides: settings, configureServices: configure);
        var browser = new Browser(app);
        _disposables.Add(app);
        _disposables.Add(browser);

        return (app, browser);
    }

    private static async Task AddAsync(BaGetApplication app, string id, long downloads, int year)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        #pragma warning disable CS8602
        context.Packages.Add(new Package
        {
            Id = id,
            Version = NuGetVersion.Parse("1.0.0"),
            Authors = new[] { "An Author" },
            Description = "Description",
            Listed = true,
            Downloads = downloads,
            SemVerLevel = SemVerLevel.Unknown,
            Published = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Summary = "Summary",
            Tags = Array.Empty<string>(),
            Dependencies = new List<PackageDependency>(),
            PackageTypes = new List<PackageType>(),
            TargetFrameworks = new List<TargetFramework>(),
        });
        #pragma warning restore CS8602
        await context.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task AddVersionAsync(BaGetApplication app, string id, string version, int year)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        var parsed = NuGetVersion.Parse(version);

        #pragma warning disable CS8602
        context.Packages.Add(new Package
        {
            Id = id,
            Version = parsed,
            Authors = new[] { "An Author" },
            Description = "Description",
            Listed = true,
            IsPrerelease = parsed.IsPrerelease,
            SemVerLevel = SemVerLevel.Unknown,
            Published = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Summary = "Summary",
            Tags = Array.Empty<string>(),
            Dependencies = new List<PackageDependency>(),
            PackageTypes = new List<PackageType>(),
            TargetFrameworks = new List<TargetFramework>(),
        });
        #pragma warning restore CS8602
        await context.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task SeedAsync(BaGetApplication app)
    {
        await AddAsync(app, "Sortable.Alpha", downloads: 10, year: 2023);
        await AddAsync(app, "Sortable.Beta", downloads: 900, year: 2021);
        await AddAsync(app, "Sortable.Gamma", downloads: 50, year: 2022);
    }

    private static string[] OrderOf(string html)
        => new[] { "Sortable.Alpha", "Sortable.Beta", "Sortable.Gamma" }
            .OrderBy(id => html.IndexOf($"/packages/{id}\"", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    [Fact]
    public async Task DefaultOrderIsById()
    {
        var (app, browser) = Start();
        await SeedAsync(app);

        var html = await browser.GetPageAsync("?q=Sortable");

        Assert.Equal(new[] { "Sortable.Alpha", "Sortable.Beta", "Sortable.Gamma" }, OrderOf(html));
    }

    [Fact]
    public async Task SortByDownloadsOrdersTheWholeResultSet()
    {
        var (app, browser) = Start();
        await SeedAsync(app);

        var html = await browser.GetPageAsync("?q=Sortable&sortBy=TotalDownloads");

        Assert.Equal(new[] { "Sortable.Beta", "Sortable.Gamma", "Sortable.Alpha" }, OrderOf(html));
    }

    [Fact]
    public async Task SortByLastUpdatedPutsTheNewestFirst()
    {
        var (app, browser) = Start();
        await SeedAsync(app);

        var html = await browser.GetPageAsync("?q=Sortable&sortBy=LastUpdated");

        Assert.Equal(new[] { "Sortable.Alpha", "Sortable.Gamma", "Sortable.Beta" }, OrderOf(html));
    }

    [Theory]
    [InlineData(false, "Stale.B", "Stale.A", "2021-01-01", "2024-01-01")]
    [InlineData(true, "Stale.A", "Stale.B", "2024-01-01", "2021-01-01")]
    public async Task TheCardAndTheOrderAgreeWithThePrereleaseFilter(bool prerelease, string first, string second, string aShows, string aHides)
    {
        var (app, browser) = Start();
        await AddVersionAsync(app, "Stale.A", "1.0.0", 2021);
        await AddVersionAsync(app, "Stale.A", "2.0.0-beta", 2024);
        await AddVersionAsync(app, "Stale.B", "1.0.0", 2023);

        var html = await browser.GetPageAsync($"?q=Stale&prerelease={prerelease.ToString().ToLowerInvariant()}&sortBy=LastUpdated");

        Assert.True(
            html.IndexOf($"/packages/{first}\"", StringComparison.OrdinalIgnoreCase)
            < html.IndexOf($"/packages/{second}\"", StringComparison.OrdinalIgnoreCase));

        var cardOfA = html.Substring(html.IndexOf("/packages/Stale.A\"", StringComparison.OrdinalIgnoreCase));
        var next = cardOfA.IndexOf("/packages/Stale.B\"", StringComparison.OrdinalIgnoreCase);
        if (next > 0) cardOfA = cardOfA.Substring(0, next);
        Assert.Contains($"title=\"{aShows} ", cardOfA);
        Assert.DoesNotContain($"title=\"{aHides} ", cardOfA);
    }

    [Fact]
    public async Task AnUnknownSortIsRefused()
    {
        var (_, browser) = Start();

        using var response = await browser.GetAsync("?q=Sortable&sortBy=Bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TheControlOffersEverySortAndKeepsTheChoiceInThePagerLinks()
    {
        var (app, browser) = Start();
        await SeedAsync(app);

        var html = await browser.GetPageAsync("?q=Sortable&sortBy=TotalDownloads");

        Assert.Contains("id=\"search-sort\"", html);
        Assert.Contains("sortBy=LastUpdated", html);
        Assert.Contains("Most downloaded", html);
        Assert.Contains("name=\"sortBy\" value=\"TotalDownloads\"", html);
    }

    [Fact]
    public async Task AProviderThatCannotSortHidesTheControlAndKeepsItsOwnOrder()
    {
        var (app, browser) = Start(
            configure: services => services.AddTransient<ISearchService>(
                provider => new UnsortableSearch(provider.GetRequiredService<DatabaseSearchService>())));
        await SeedAsync(app);

        var html = await browser.GetPageAsync("?q=Sortable&sortBy=TotalDownloads");

        Assert.DoesNotContain("id=\"search-sort\"", html);
        Assert.DoesNotContain("name=\"sortBy\"", html);
        Assert.Equal(new[] { "Sortable.Alpha", "Sortable.Beta", "Sortable.Gamma" }, OrderOf(html));
    }

    [Fact]
    public async Task TheV3SearchApiIgnoresSortAndIsUnchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (app, _) = Start();
        await SeedAsync(app);
        using var client = app.CreateClient();

        var plain = await client.GetStringAsync("v3/search?q=Sortable", cancellationToken);
        var withSort = await client.GetStringAsync("v3/search?q=Sortable&sortBy=TotalDownloads&sort=LastUpdated", cancellationToken);

        Assert.Equal(plain, withSort);
        Assert.True(plain.IndexOf("Sortable.Alpha", StringComparison.Ordinal) < plain.IndexOf("Sortable.Beta", StringComparison.Ordinal));
        Assert.DoesNotContain("\"sortBy", plain, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A provider that cannot sort: reports relevance only and ignores the request, as the
    /// Azure providers do.
    /// </summary>
    private sealed class UnsortableSearch : ISearchService
    {
        private readonly ISearchService _inner;

        public UnsortableSearch(ISearchService inner) => _inner = inner;

        public IReadOnlyCollection<SearchSort> SupportedSorts => new[] { SearchSort.Relevance };

        public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
        {
            // What a provider that cannot sort does: pay no attention to SortBy. The page
            // must not have asked for one it cannot get.
            Assert.Equal(SearchSort.Relevance, request.SortBy);
            return _inner.SearchAsync(request, cancellationToken);
        }

        public Task<AutocompleteResponse> AutocompleteAsync(AutocompleteRequest request, CancellationToken cancellationToken)
            => _inner.AutocompleteAsync(request, cancellationToken);

        public Task<AutocompleteResponse> ListPackageVersionsAsync(VersionsRequest request, CancellationToken cancellationToken)
            => _inner.ListPackageVersionsAsync(request, cancellationToken);

        public Task<DependentsResponse> FindDependentsAsync(string? packageId, CancellationToken cancellationToken)
            => _inner.FindDependentsAsync(packageId, cancellationToken);
    }
}
