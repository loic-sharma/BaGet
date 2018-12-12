using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using BaGet.Linq;
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
            var packages = _context.Query<Package>();
            var search = packages;

            if (!string.IsNullOrEmpty(query))
            {
                query = query.ToLower();
                search = search.Where(p => p.PackageId.Contains(query));
            }

            var packagesResult = await search
                .OrderBy(s => s.PackageId)
                .Select(s => s.PackageId)
                .Distinct()
                .Skip(skip)
                .Take(take)
                .ToListAsync();

            var packageVersions = packages
                .Where(s => packagesResult.Contains(s.PackageId))
                .Select(s => new
                {
                    s.PackageId,
                    s.Version,
                    s.Downloads
                })
                .ToList()
                .GroupBy(s => s.PackageId)
                .Select(q => new
                {
                    PackageId = q.Key,
                    Versions = q.Select(s => new { s.Version, s.Downloads }).ToList()
                })
                .ToList();

            var packageMaxVersions = packageVersions
                .Select(s => new
                {
                    s.PackageId,
                    Version = s.Versions.Select(t => NuGetVersion.Parse(t.Version)).Max()
                })
                .ToArray();

            var maxVersions = packageMaxVersions
                .Select(s => s.Version.ToString())
                .ToArray();

            var packagesInfo = packages.Where(s => packagesResult.Contains(s.PackageId) && maxVersions.Contains(s.Version))
                .Select(s => new
                {
                    s.PackageId,
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
            IReadOnlyList<SearchResult> result = packagesInfo.Join(packageVersions, s => s.PackageId, s => s.PackageId, (i, v) => new SearchResult
            {
                Authors = i.Authors,
                Description = i.Description,
                Id = i.PackageId,
                IconUrl = i.IconUrl,
                LicenseUrl = i.LicenseUrl,
                ProjectUrl = i.ProjectUrl,
                Summary = i.Summary,
                Tags = i.Tags.Select(s => s.Tag).ToArray(),
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
            var search = _context.Query<Package>();

            if (!string.IsNullOrEmpty(query))
            {
                query = query.ToLower();
                search = search.Where(p => p.PackageId.ToLower().Contains(query));
            }

            var results = await search.Where(p => p.Listed)
                .OrderByDescending(p => p.Downloads)
                .Skip(skip)
                .Take(take)
                .Select(p => p.PackageId)
                .Distinct()
                .ToListAsync();

            return results.AsReadOnly();
        }
    }
}
