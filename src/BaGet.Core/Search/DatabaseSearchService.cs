using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using BaGet.Core.Indexing;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core.Search
{
    public class DatabaseSearchService : ISearchService
    {
        private readonly IContext _context;
        private readonly IFrameworkCompatibilityService _frameworks;

        public DatabaseSearchService(IContext context, IFrameworkCompatibilityService frameworks)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _frameworks = frameworks ?? throw new ArgumentNullException(nameof(frameworks));
        }

        public Task IndexAsync(Package package) => Task.CompletedTask;

        public async Task<IReadOnlyList<SearchResult>> SearchAsync(
            string query,
            int skip = 0,
            int take = 20,
            bool includePrerelease = true,
            bool includeSemVer2 = true,
            string packageType = null,
            string framework = null)
        {
            var result = new List<SearchResult>();
            var frameworks = GetCompatibleFrameworks(framework);
            var packages = await SearchImplAsync(query, skip, take, includePrerelease, includeSemVer2, packageType, frameworks);

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

        private IReadOnlyList<string> GetCompatibleFrameworks(string framework)
        {
            if (framework == null) return null;

            return _frameworks.FindAllCompatibleFrameworks(framework);
        }

        private async Task<List<IGrouping<string, Package>>> SearchImplAsync(
            string query,
            int skip,
            int take,
            bool includePrerelease,
            bool includeSemVer2,
            string packageType,
            IReadOnlyList<string> frameworks)
        {
            IQueryable<Package> search = _context.Packages.Where(p => p.Listed);

            if (!string.IsNullOrEmpty(query))
            {
                query = query.ToLower();
                search = search.Where(p => p.Id.ToLower().Contains(query));
            }

            if (!includePrerelease)
            {
                search = search.Where(p => !p.IsPrerelease);
            }

            if (!includeSemVer2)
            {
                search = search.Where(p => p.SemVerLevel != SemVerLevel.SemVer2);
            }

            if (!string.IsNullOrEmpty(packageType))
            {
                search = search.Where(p => p.PackageTypes.Any(t => t.Name == packageType));
            }

            if (frameworks != null)
            {
                search = search.Where(p => p.TargetFrameworks.Any(f => frameworks.Contains(f.Moniker)));
            }

            var packageIds = search.Select(p => p.Id)
                .OrderBy(id => id)
                .Distinct()
                .Skip(skip)
                .Take(take);

            // This query MUST fetch all versions for each package that matches the search,
            // otherwise the results for a package's latest version may be incorrect.
            // If possible, we'll find all these packages in a single query by matching
            // the package IDs in a subquery. Otherwise, run two queries:
            //   1. Find the package IDs that match the search
            //   2. Find all package versions for these package IDs
            if (_context.SupportsLimitInSubqueries)
            {
                search = _context.Packages.Where(p => packageIds.Contains(p.Id));
            }
            else
            {
                var packageIdResults = await packageIds.ToListAsync();

                search = _context.Packages.Where(p => packageIdResults.Contains(p.Id));
            }

            return await search.GroupBy(p => p.Id).ToListAsync();
        }

        public async Task<IReadOnlyList<string>> AutocompleteAsync(string query, int skip = 0, int take = 20)
        {
            IQueryable<Package> search = _context.Packages;

            if (!string.IsNullOrEmpty(query))
            {
                query = query.ToLower();
                search = search.Where(p => p.Id.ToLower().Contains(query));
            }

            return await search.Where(p => p.Listed)
                .OrderByDescending(p => p.Downloads)
                .Skip(skip)
                .Take(take)
                .Select(p => p.Id)
                .Distinct()
                .ToListAsync();
        }

        public async Task<IReadOnlyList<string>> FindDependentsAsync(string packageId, int skip = 0, int take = 20)
        {
            return await _context
                .Packages
                .Where(p => p.Listed)
                .OrderByDescending(p => p.Downloads)
                .Where(p => p.Dependencies.Any(d => d.Id == packageId))
                .Skip(skip)
                .Take(take)
                .Select(p => p.Id)
                .Distinct()
                .ToListAsync();
        }
    }
}
