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
        private readonly BaGetContext _context;

        public SearchController(BaGetContext context)
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
                .Take(20)
                .ToListAsync();

            return new
            {
                TotalHits = flatResults.Count,
                Data = flatResults
                    .GroupBy(p => p.Id)
                    .Select(g => new SearchResult(
                        id: g.Key,
                        latest: g.OrderBy(p => p.Version).First(),
                        versions: g.Select(p => p.VersionString).ToList(),
                        registrationIndex: Url.PackageRegistrationIndex(g.Key)))
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

            var results = await search.Where(p => p.Listed)
                .Select(p => p.Id)
                .Distinct()
                .Take(20)
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
                IReadOnlyList<string> versions,
                string registrationIndex)
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
                TotalDownloads = 0;
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
            public int TotalDownloads { get; }

            // This is wrong
            public IReadOnlyList<string> Versions { get; }
        }
    }
}