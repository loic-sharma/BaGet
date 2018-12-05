using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core.Services
{
    public class DatabaseSearchService : ISearchService
    {
        private readonly IContext _context;

        public DatabaseSearchService(IContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public Task IndexAsync(Package package) => Task.CompletedTask;

        public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int skip = 0, int take = 20)
        {
            IQueryable<Package> search = _context.Packages;

            if (!string.IsNullOrEmpty(query))
            {
                query = query.ToLower();
                search = search.Where(p => p.Id.ToLower().Contains(query));
            }

            var packages = await _context.Packages
                .Where(p =>
                    search.Select(p2 => p2.Id)
                        .OrderBy(id => id)
                        .Distinct()
                        .Skip(skip)
                        .Take(take)
                        .Contains(p.Id))
                .GroupBy(p => p.Id)
                .ToListAsync();

            var result = new List<SearchResult>();

            foreach (var package in packages)
            {
                var versions = package.OrderByDescending(p => p.Version).ToList();
                var latest = versions.First();

                var versionResults = versions.Select(p => new SearchResultVersion(p.Version, p.Downloads));

                result.Add(new SearchResult
                {
                    Id = latest.Id,
                    Version = latest.Version,
                    Description = latest.Description,
                    Authors = latest.Authors,
                    IconUrl = latest.IconUrlString,
                    LicenseUrl = latest.LicenseUrlString,
                    ProjectUrl = latest.ProjectUrlString,
                    Summary = latest.Summary,
                    Tags = latest.Tags,
                    Title = latest.Title,
                    TotalDownloads = versions.Sum(p => p.Downloads),
                    Versions = versionResults.ToList().AsReadOnly(),
                });
            }

            return result.AsReadOnly();
        }

        public async Task<IReadOnlyList<string>> AutocompleteAsync(string query, int skip = 0, int take = 20)
        {
            IQueryable<Package> search = _context.Packages;

            if (!string.IsNullOrEmpty(query))
            {
                query = query.ToLower();
                search = search.Where(p => p.Id.ToLower().Contains(query));
            }

            var results = await search.Where(p => p.Listed)
                .OrderByDescending(p => p.Downloads)
                .Skip(skip)
                .Take(take)
                .Select(p => p.Id)
                .Distinct()
                .ToListAsync();

            return results.AsReadOnly();
        }
    }
}
