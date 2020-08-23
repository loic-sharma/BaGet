using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Protocol.Models;
using Microsoft.AspNetCore.Mvc;

namespace BaGet.Hosting
{
    public class SearchController : Controller
    {
        private readonly ISearchService _searchService;

        public SearchController(ISearchService searchService)
        {
            _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        }

        public async Task<ActionResult<SearchResponse>> SearchAsync(
            [FromQuery(Name = "q")] string query = null,
            [FromQuery]int skip = 0,
            [FromQuery]int take = 20,
            [FromQuery]bool prerelease = false,
            [FromQuery]string semVerLevel = null,

            // These are unofficial parameters
            [FromQuery]string packageType = null,
            [FromQuery]string framework = null,
            CancellationToken cancellationToken = default)
        {
            var request = new SearchRequest
            {
                Skip = skip,
                Take = take,
                IncludePrerelease = prerelease,
                IncludeSemVer2 = semVerLevel == "2.0.0",
                PackageType = packageType,
                Framework = framework,
                Query = query ?? string.Empty,
            };

            return await _searchService.SearchAsync(request, cancellationToken);
        }

        public async Task<ActionResult<AutocompleteResponse>> AutocompleteAsync(
            [FromQuery(Name = "q")] string autocompleteQuery = null,
            [FromQuery(Name = "id")] string versionsQuery = null,
            [FromQuery]bool prerelease = false,
            [FromQuery]string semVerLevel = null,
            [FromQuery]int skip = 0,
            [FromQuery]int take = 20,

            // These are unofficial parameters
            [FromQuery]string packageType = null,
            CancellationToken cancellationToken = default)
        {
            var request = new AutocompleteRequest
            {
                IncludePrerelease = prerelease,
                IncludeSemVer2 = semVerLevel == "2.0.0",
                PackageType = packageType,
                Skip = skip,
                Take = take
            };

            // Default to autocomplete, just like nuget.org does
            if (versionsQuery != null && autocompleteQuery == null)
            {
                request.Query = versionsQuery;

                return await _searchService.ListPackageVersionsAsync(request, cancellationToken);
            }
            else
            {
                request.Query = autocompleteQuery;

                return await _searchService.AutocompleteAsync(request, cancellationToken);
            }
        }

        public async Task<ActionResult<DependentsResponse>> DependentsAsync(
            [FromQuery] string packageId,
            CancellationToken cancellationToken = default)
        {
            return await _searchService.FindDependentsAsync(packageId, cancellationToken);
        }
    }
}
