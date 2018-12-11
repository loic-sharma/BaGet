using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using Microsoft.EntityFrameworkCore;
using NuGet.Versioning;

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
            // do we need a filter for listed?
            IQueryable<Package> packages = _context.Packages;
            IQueryable<Package> search = packages;

            if (!string.IsNullOrEmpty(query))
            {
                query = query.ToLower();
                search = search.Where(p => p.Id.ToLower().Contains(query));
            }

            var packageVersions = await search
                .GroupBy(s => s.Id)
                .Select(s => new
                {
                    Id = s.Key,
                    Versions = s.Select(q => new { q.Version, q.Downloads }).ToList(),
                })
                .OrderBy(s => s.Id)
                .Skip(skip)
                .Take(take)
                .ToListAsync();

            var packageMaxVersions = packageVersions
                .Select(s => new
                {
                    s.Id,
                    Version = s.Versions.Select(t => NuGetVersion.Parse(t.Version)).Max()
                })
                .ToArray();

            var ids = packageMaxVersions.Select(s => s.Id)
                .ToArray();

            var maxVersions = packageMaxVersions
                .Select(s => s.Version.ToString())
                .ToArray();

            var packagesInfo = packages.Where(s => ids.Contains(s.Id) && maxVersions.Contains(s.Version))
                .Select(s => new
                {
                    s.Id,
                    s.Version,
                    s.Description,
                    s.Authors,
                    s.IconUrl,
                    s.LicenseUrl,
                    s.ProjectUrl,
                    s.Summary,
                    s.Tags,
                    s.Title
                })
                .ToList();

            // actually one to one
            IReadOnlyList<SearchResult> result = packagesInfo.Join(packageVersions, s => s.Id, s => s.Id, (i, v) => new SearchResult
            {
                Authors = i.Authors,
                Description = i.Description,
                Id = i.Id,
                IconUrl = i.IconUrl,
                LicenseUrl = i.LicenseUrl,
                ProjectUrl = i.ProjectUrl,
                Summary = i.Summary,
                Tags = i.Tags,
                Title = i.Title,
                TotalDownloads = v.Versions.Sum(w => w.Downloads),
                Version = NuGetVersion.Parse(i.Version),
                Versions = v.Versions.Select(q => new SearchResultVersion(NuGetVersion.Parse(q.Version), q.Downloads)).ToList()
            })
            .ToList()
            .AsReadOnly();

            return result;
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
