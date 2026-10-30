using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using BaGet.Protocol.Models;
using BaGet.Web.Application.Models.Browse;
using BaGet.Web.Application.Screens;

namespace BaGet.Web.Application.UseCases;

/// <summary>
/// The front page and the package list (<c>/</c>, <c>/packages</c>, <c>/?q=…</c>, and under <c>/r/{repo}/</c>) of a NuGet repository: the
/// totals on the front page, else one page of search results.
/// </summary>
public sealed class IndexUseCase
{
    /// <summary>Results per page.</summary>
    public const int ResultsPerPage = 20;

    private readonly ISearchService _search;
    private readonly StatisticsService _statistics;

    /// <summary>Creates the use case.</summary>
    public IndexUseCase(ISearchService search, StatisticsService statistics)
    {
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
    }

    /// <summary>The orders the search provider can apply (only relevance when it says nothing).</summary>
    public IReadOnlyCollection<SearchSort> SupportedSorts
        => _search.SupportedSorts ?? new[] { SearchSort.Relevance };

    /// <summary>False when the query cannot be answered (a sort that is not a defined value, a page below 1): the screen answers 400.</summary>
    public static bool IsValid(IndexQuery query)
        => Enum.IsDefined(typeof(SearchSort), query.SortBy) && query.PageIndex >= 1;

    /// <summary>The front page (when the query asks for nothing) or one page of results.</summary>
    /// <param name="query">What was asked for.</param>
    /// <param name="atRepositoryRoot">True when the address is the repository's root without a <c>q</c> parameter: only then can the screen be the front page.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    public async Task<ScreenResult<IndexScreen>> GetAsync(IndexQuery query, bool atRepositoryRoot, CancellationToken cancellationToken)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));

        var supported = SupportedSorts.ToList();
        var screen = new IndexScreen
        {
            Query = query.Query,
            PageIndex = query.PageIndex,
            PackageType = query.PackageType,
            Framework = query.Framework,
            Prerelease = query.Prerelease,
            SortBy = query.SortBy,
            SupportedSorts = supported,
        };

        if (IsFrontPage(query, atRepositoryRoot))
        {
            return ScreenResult<IndexScreen>.Ok(screen with
            {
                IsHome = true,
                Totals = _statistics.IsSupported ? await _statistics.GetAsync(cancellationToken) : null,
            });
        }

        var packageType = query.PackageType == "any" ? null : query.PackageType;
        var framework = query.Framework == "any" ? null : query.Framework;
        var canSort = supported.Count > 1;

        var search = await _search.SearchAsync(
            new SearchRequest
            {
                Skip = (query.PageIndex - 1) * ResultsPerPage,
                Take = ResultsPerPage,
                IncludePrerelease = query.Prerelease,
                IncludeSemVer2 = true,
                PackageType = packageType,
                Framework = framework,
                Query = query.Query,

                // A provider that cannot sort ignores the request, so the page stays honest
                // about it by not offering the choice.
                SortBy = canSort && supported.Contains(query.SortBy) ? query.SortBy : SearchSort.Relevance,
            },
            cancellationToken);

        IReadOnlyList<SearchResult> packages = search.Data ?? (IReadOnlyList<SearchResult>)Array.Empty<SearchResult>();
        var lastUpdated = await _statistics.GetLastUpdatedAsync(
            packages.Select(p => p.PackageId).ToList(),
            cancellationToken,
            includePrerelease: query.Prerelease,
            includeSemVer2: true);

        return ScreenResult<IndexScreen>.Ok(screen with { Packages = packages, LastUpdated = lastUpdated });
    }

    private static bool IsFrontPage(IndexQuery query, bool atRepositoryRoot)
        => atRepositoryRoot
           && string.IsNullOrWhiteSpace(query.Query)
           && query.PackageType == "any"
           && query.Framework == "any"
           && query.Prerelease
           && query.PageIndex == 1;
}
