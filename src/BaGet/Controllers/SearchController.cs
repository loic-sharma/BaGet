using System;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core.Services;
using BaGet.Extensions;
using BaGet.Protocol;
using Microsoft.AspNetCore.Mvc;

namespace BaGet.Controllers
{
    using ProtocolSearchResult = Protocol.SearchResult;
    using QuerySearchResult = Core.Services.SearchResult;

    public class SearchController : Controller
    {
        private readonly ISearchService _searchService;

        public SearchController(ISearchService searchService)
        {
            _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        }

        public async Task<IActionResult> Get([FromQuery(Name = "q")] string query = null)
        {
            query = query ?? string.Empty;

            var results = await _searchService.SearchAsync(query);
            var response = new SearchResponse(
                totalHits: results.Count,
                data: results.Select(ToSearchResult).ToList());

            return Json(response);
        }

        public async Task<IActionResult> Autocomplete([FromQuery(Name = "q")] string query = null)
        {
            var results = await _searchService.AutocompleteAsync(query);
            var response = new AutocompleteResult(results.Count, results);

            return Json(response);
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