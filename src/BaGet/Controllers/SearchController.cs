using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Core.Entities;
using BaGet.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace BaGet.Controllers
{
    public class SearchController : Controller
    {
        private readonly IContext _context;

        public SearchController(IContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<object> Get([FromQuery(Name = "q")] string query = null)
        {
            IQueryable<Package> search = _context.Packages;

            if (!string.IsNullOrEmpty(query))
            {
                query = query.ToLower();
                search = search.Where(p => p.Id.ToLower().Contains(query));
            }

            var flatResults = await search
                .OrderByDescending(p => p.Downloads)
                .Take(20)
                .ToListAsync();

            return new
            {
                TotalHits = flatResults.Count,
                Data = flatResults
                    .GroupBy(p => p.Id)
                    .Select(g => new SearchResult(
                        id: g.Key,
                        latest: g.OrderByDescending(p => p.Version).First(),
                        versions: g.Select(p => new SearchResultVersion(
                            registrationUrl: Url.PackageRegistration(p.Id, p.Version),
                            version: p.VersionString,
                            downloads: p.Downloads)).ToList(),
                        registrationIndex: Url.PackageRegistration(g.Key),
                        totalDownloads: g.Sum(p => p.Downloads)))
            };
        }

        public async Task<IActionResult> Autocomplete([FromQuery(Name = "q")] string query = null)
        {
            IQueryable<Package> search = _context.Packages;

            if (!string.IsNullOrEmpty(query))
            {
                query = query.ToLower();
                search = search.Where(p => p.Id.ToLower().Contains(query));
            }

            // TODO: Order by downloads
            var results = await search.Where(p => p.Listed)
                .OrderByDescending(p => p.Published)
                .Take(20)
                .Select(p => p.Id)
                .Distinct()
                .ToListAsync();

            return Json(new
            {
                TotalHits = results.Count,
                Data = results,
            });
        }

        private class SearchResult
        {
            public SearchResult(
                string id,
                Package latest,
                IReadOnlyList<SearchResultVersion> versions,
                string registrationIndex,
                long totalDownloads)
            {
                PackageId = id;
                Version = latest.VersionString;
                Description = latest.Description;
                Versions = versions;
                Authors = latest.Authors;
                IconUrl = latest.IconUrlString;
                LicenseUrl = latest.LicenseUrlString;
                ProjectUrl = latest.ProjectUrlString;
                Registration = registrationIndex;
                Summary = latest.Summary;
                Tags = latest.Tags;
                Title = latest.Title;
                TotalDownloads = totalDownloads;
            }

            [JsonProperty(PropertyName = "id")]
            public string PackageId { get; }

            public string Version { get; }

            public string Description { get; }
            public string Authors { get; }
            public string IconUrl { get; }
            public string LicenseUrl { get; }
            public string ProjectUrl { get; }
            public string Registration { get; }
            public string Summary { get; }
            public string[] Tags { get; }
            public string Title { get; }
            public long TotalDownloads { get; }

            public IReadOnlyList<SearchResultVersion> Versions { get; }
        }

        private class SearchResultVersion
        {
            public SearchResultVersion(string registrationUrl, string version, long downloads)
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