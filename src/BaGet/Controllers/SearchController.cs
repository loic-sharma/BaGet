using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using BaGet.Core.Services;
using BaGet.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace BaGet.Controllers
{
    public class SearchController : Controller
    {
        private readonly ISearchService _searchService;

        public SearchController(ISearchService searchService)
        {
            _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        }

        public async Task<object> Get([FromQuery(Name = "q")] string query = null)
        {
            query = query ?? string.Empty;

            var results = await _searchService.SearchAsync(query);

            return new
            {
                TotalHits = results.Count,
                Data = results.Select(p => new SearchResultModel(p, Url))
            };
        }

        public async Task<IActionResult> Autocomplete([FromQuery(Name = "q")] string query = null)
        {
            var results = await _searchService.AutocompleteAsync(query);

            return Json(new
            {
                TotalHits = results.Count,
                Data = results,
            });
        }

        private class SearchResultModel
        {
            private readonly SearchResult _result;
            private readonly IUrlHelper _url;

            public SearchResultModel(SearchResult result, IUrlHelper url)
            {
                _result = result ?? throw new ArgumentNullException(nameof(result));
                _url = url ?? throw new ArgumentNullException(nameof(url));

                var versions = result.Versions.Select(
                    v => new SearchResultVersionModel(
                        url.PackageRegistration(result.Id, v.Version),
                        v.Version.ToNormalizedString(),
                        v.Downloads));

                Versions = versions.ToList().AsReadOnly();
            }

            public string Id => _result.Id;
            public string Version => _result.Version.ToNormalizedString();
            public string Description => _result.Description;
            public string Authors => _result.Authors;
            public string IconUrl => _result.IconUrl;
            public string LicenseUrl => _result.LicenseUrl;
            public string ProjectUrl => _result.ProjectUrl;
            public string Registration => _url.PackageRegistration(_result.Id);
            public string Summary => _result.Summary;
            public string[] Tags => _result.Tags;
            public string Title => _result.Title;
            public long TotalDownloads => _result.TotalDownloads;

            public IReadOnlyList<SearchResultVersionModel> Versions { get; }
        }

        private class SearchResultVersionModel
        {
            public SearchResultVersionModel(string registrationUrl, string version, long downloads)
            {
                if (string.IsNullOrEmpty(registrationUrl)) throw new ArgumentNullException(nameof(registrationUrl));
                if (string.IsNullOrEmpty(version)) throw new ArgumentNullException(nameof(version));

                RegistrationUrl = registrationUrl;
                Version = version;
                Downloads = downloads;
            }

            [JsonProperty(PropertyName = "id")]
            public string RegistrationUrl { get; }

            public string Version { get; }

            public long Downloads { get; }
        }
    }
}