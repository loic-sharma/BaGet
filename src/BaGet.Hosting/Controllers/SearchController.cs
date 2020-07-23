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
            var includeSemVer2 = semVerLevel == "2.0.0";

            return await _searchService.SearchAsync(
                query ?? string.Empty,
                skip,
                take,
                prerelease,
                includeSemVer2,
                packageType,
                framework,
                cancellationToken);
        }

        public async Task<ActionResult<AutocompleteResponse>> AutocompleteAsync(
            [FromQuery(Name = "q")] string query = null,
            [FromQuery]int skip = 0,
            [FromQuery]int take = 20,
            [FromQuery]bool prerelease = false,
            [FromQuery]string semVerLevel = null,

            // These are unofficial parameters
            [FromQuery]string packageType = null,
            CancellationToken cancellationToken = default)
        {
            var includeSemVer2 = semVerLevel == "2.0.0";

            // TODO: Add other autocomplete parameters
            // TODO: Support versions autocomplete.
            // See: https://github.com/loic-sharma/BaGet/issues/291
            return await _searchService.AutocompleteAsync(
                query,
                skip,
                take,
                prerelease,
                includeSemVer2,
                packageType,
                cancellationToken: cancellationToken);
        }

        public async Task<ActionResult<DependentsResponse>> DependentsAsync(
            [FromQuery] string packageId,
            CancellationToken cancellationToken = default)
        {
            return await _searchService.FindDependentsAsync(packageId, cancellationToken);
        }
    }
}
