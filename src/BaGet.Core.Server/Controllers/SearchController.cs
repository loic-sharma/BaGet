using System;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core.Search;
using BaGet.Extensions;
using BaGet.Protocol;
using Microsoft.AspNetCore.Mvc;

namespace BaGet.Controllers
{
    using ProtocolSearchResult = Protocol.SearchResult;
    using QuerySearchResult = Core.Search.SearchResult;

    public class SearchController : Controller
    {
        private readonly ISearchService _searchService;

        public SearchController(ISearchService searchService)
        {
            _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        }

        public async Task<ActionResult<SearchResponse>> Get(
            [FromQuery(Name = "q")] string query = null,
            [FromQuery]int skip = 0,
            [FromQuery]int take = 20,
            [FromQuery]bool prerelease = false,
            [FromQuery]string semVerLevel = null,

            // These are unofficial parameters
            [FromQuery]string packageType = null,
            [FromQuery]string framework = null)
        {
            var includeSemVer2 = semVerLevel == "2.0.0";
            var results = await _searchService.SearchAsync(
                query ?? string.Empty,
                skip,
                take,
                prerelease,
                includeSemVer2,
                packageType,
                framework);

            return new SearchResponse(
                totalHits: results.Count,
                data: results.Select(ToSearchResult).ToList(),
                context: SearchContext.Default(Url.RegistrationsBase()));
        }

        public async Task<ActionResult<AutocompleteResult>> Autocomplete([FromQuery(Name = "q")] string query = null)
        {
            var results = await _searchService.AutocompleteAsync(query);

            return new AutocompleteResult(
                results.Count,
                results,
                AutocompleteContext.Default);
        }

        public async Task<ActionResult<DependentResult>> Dependents([FromQuery(Name = "packageId")] string packageId)
        {
            var results = await _searchService.FindDependentsAsync(packageId);

            return new DependentResult(results.Count, results);
        }

        private ProtocolSearchResult ToSearchResult(QuerySearchResult result)
        {
            var versions = result.Versions.Select(
                v => new Protocol.SearchResultVersion(
                    registrationLeafUrl: Url.PackageRegistration(result.Id, v.Version),
                    version: v.Version,
                    downloads: v.Downloads));

            return new ProtocolSearchResult(
                id: result.Id,
                version: result.Version,
                description: result.Description,
                authors: result.Authors,
                iconUrl: result.IconUrl,
                licenseUrl: result.LicenseUrl,
                projectUrl: result.ProjectUrl,
                registrationUrl: Url.PackageRegistration(result.Id),
                summary: result.Summary,
                tags: result.Tags,
                title: result.Title,
                totalDownloads: result.TotalDownloads,
                versions: versions.ToList());
        }
    }
}
