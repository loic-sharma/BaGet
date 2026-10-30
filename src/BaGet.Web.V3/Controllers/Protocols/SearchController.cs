using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Protocol.Models;
using Microsoft.AspNetCore.Mvc;

namespace BaGet.Web.V3.Controllers.Protocols;

public class SearchController : ControllerBase
{
    private readonly ISearchService _searchService;

    public SearchController(ISearchService searchService)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
    }

    /// <summary>The largest page a caller can ask for; the V2 feed clamps to the same figure.</summary>
    internal const int MaxTake = 1000;

    internal static int ClampSkip(int skip) => Math.Max(skip, 0);

    internal static int ClampTake(int take) => Math.Clamp(take, 0, MaxTake);

    public async Task<ActionResult<SearchResponse>> SearchAsync(
        [FromQuery(Name = "q")] string? query = null,
        [FromQuery]int skip = 0,
        [FromQuery]int take = 20,
        [FromQuery]bool prerelease = false,
        [FromQuery]string? semVerLevel = null,

        // These are unofficial parameters
        [FromQuery]string? packageType = null,
        [FromQuery]string? framework = null,
        CancellationToken cancellationToken = default)
    {
        var request = new SearchRequest
        {
            Skip = ClampSkip(skip),
            Take = ClampTake(take),
            IncludePrerelease = prerelease,
            IncludeSemVer2 = semVerLevel == "2.0.0",
            PackageType = packageType,
            Framework = framework,
            Query = query ?? string.Empty,
        };

        return await _searchService.SearchAsync(request, cancellationToken);
    }

    public async Task<ActionResult<AutocompleteResponse>> AutocompleteAsync(
        [FromQuery(Name = "q")] string? autocompleteQuery = null,
        [FromQuery(Name = "id")] string? versionsQuery = null,
        [FromQuery]bool prerelease = false,
        [FromQuery]string? semVerLevel = null,
        [FromQuery]int skip = 0,
        [FromQuery]int take = 20,

        // These are unofficial parameters
        [FromQuery]string? packageType = null,
        CancellationToken cancellationToken = default)
    {
        // If only "id" is provided, find package versions. Otherwise, find package IDs.
        if (versionsQuery != null && autocompleteQuery == null)
        {
            var request = new VersionsRequest
            {
                IncludePrerelease = prerelease,
                IncludeSemVer2 = semVerLevel == "2.0.0",
                PackageId = versionsQuery,
            };

            return await _searchService.ListPackageVersionsAsync(request, cancellationToken);
        }
        else
        {
            var request = new AutocompleteRequest
            {
                IncludePrerelease = prerelease,
                IncludeSemVer2 = semVerLevel == "2.0.0",
                PackageType = packageType,
                Skip = ClampSkip(skip),
                Take = ClampTake(take),
                Query = autocompleteQuery,
            };

            return await _searchService.AutocompleteAsync(request, cancellationToken);
        }
    }

    public async Task<ActionResult<DependentsResponse>> DependentsAsync(
        [FromQuery] string? packageId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return BadRequest();
        }

        return await _searchService.FindDependentsAsync(packageId, cancellationToken);
    }
}
