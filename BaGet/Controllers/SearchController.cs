using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Core.Entities;
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

            var results = await search.Take(20).ToListAsync();

            results.GroupBy(p => p.Id);

            return new
            {
                TotalHits = results.Count,
                Data = results.GroupBy(p => p.Id)
                    .Select(g => new SearchResult(
                        g.Key,
                        g.Max(p => p.Version).ToNormalizedString(),
                        g.Select(p => p.Version.ToNormalizedString()).ToList()))
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
            public SearchResult(string packageId, string version, IReadOnlyList<string> versions)
            {
                if (string.IsNullOrEmpty(packageId)) throw new ArgumentNullException(nameof(packageId));
                if (string.IsNullOrEmpty(version)) throw new ArgumentNullException(nameof(version));

                PackageId = packageId;
                Version = version;
                Versions = versions ?? throw new ArgumentNullException(nameof(versions));
            }

            [JsonProperty(PropertyName = "id")]
            public string PackageId { get; }

            public string Version { get; }

            public IReadOnlyList<string> Versions { get; }
        }
    }
}