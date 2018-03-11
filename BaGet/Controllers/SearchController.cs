using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Core.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace BaGet.Controllers
{
    public class SearchController
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
                search = search.Where(p => p.Id.Contains(query));
            }

            var results = await search.Take(20).ToListAsync();

            results.GroupBy(p => p.Id);

            return new
            {
                TotalHits = results.Count,
                Data = results.GroupBy(p => p.Id)
                            .Select(g => new SearchResult(
                                                g.Key,
                                                g.Max(p => NuGetVersion.Parse(p.Version)).ToNormalizedString(),
                                                g.Select(p => p.Version).ToList()))
            };
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