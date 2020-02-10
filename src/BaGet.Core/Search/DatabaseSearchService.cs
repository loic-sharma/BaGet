using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core
{
    public class DatabaseSearchService : ISearchService
    {
        private readonly IContext _context;
        private readonly IFrameworkCompatibilityService _frameworks;
        private readonly IUrlGenerator _url;

        public DatabaseSearchService(IContext context, IFrameworkCompatibilityService frameworks, IUrlGenerator url)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _frameworks = frameworks ?? throw new ArgumentNullException(nameof(frameworks));
            _url = url ?? throw new ArgumentNullException(nameof(url));
        }

        public Task IndexAsync(Package package, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task<SearchResponse> SearchAsync(
            string query = null,
            int skip = 0,
            int take = 20,
            bool includePrerelease = true,
            bool includeSemVer2 = true,
            CancellationToken cancellationToken = default)
        {
            return await SearchAsync(
                query,
                skip,
                take,
                includePrerelease,
                includeSemVer2,
                packageType: null,
                framework: null,
                cancellationToken: cancellationToken);
        }

        public async Task<SearchResponse> SearchAsync(
            string query = null,
            int skip = 0,
            int take = 20,
            bool includePrerelease = true,
            bool includeSemVer2 = true,
            string packageType = null,
            string framework = null,
            CancellationToken cancellationToken = default)
        {
            var result = new List<SearchResult>();
            var packages = await SearchImplAsync(
                query,
                skip,
                take,
                includePrerelease,
                includeSemVer2,
                packageType,
                framework,
                cancellationToken);

            foreach (var package in packages)
            {
                var versions = package.OrderByDescending(p => p.Version).ToList();
                var latest = versions.First();
                var iconUrl = latest.HasIcon
                    ? _url.GetPackageIconDownloadUrl(latest.Id, latest.Version)
                    : latest.IconUrlString;

                result.Add(new SearchResult
                {
                    PackageId = latest.Id,
                    Version = latest.Version.ToFullString(),
                    Description = latest.Description,
                    Authors = latest.Authors,
                    IconUrl = iconUrl,
                    LicenseUrl = latest.LicenseUrlString,
                    ProjectUrl = latest.ProjectUrlString,
                    RegistrationIndexUrl = _url.GetRegistrationIndexUrl(latest.Id),
                    Summary = latest.Summary,
                    Tags = latest.Tags,
                    Title = latest.Title,
                    TotalDownloads = versions.Sum(p => p.Downloads),
                    Versions = versions
                        .Select(p => new SearchResultVersion
                        {
                            RegistrationLeafUrl = _url.GetRegistrationLeafUrl(p.Id, p.Version),
                            Version = p.Version.ToFullString(),
                            Downloads = p.Downloads,
                        })
                        .ToList()
                });
            }

            return new SearchResponse
            {
                TotalHits = result.Count,
                Data = result,
                Context = SearchContext.Default(_url.GetPackageMetadataResourceUrl())
            };
        }

        public async Task<AutocompleteResponse> AutocompleteAsync(
            string query = null,
            AutocompleteType type = AutocompleteType.PackageIds,
            int skip = 0,
            int take = 20,
            bool includePrerelease = true,
            bool includeSemVer2 = true,
            CancellationToken cancellationToken = default)
        {
            // TODO: Support versions autocomplete.
            // See: https://github.com/loic-sharma/BaGet/issues/291
            if (type != AutocompleteType.PackageIds) throw new NotImplementedException();

            IQueryable<Package> search = _context.Packages;

            if (!string.IsNullOrEmpty(query))
            {
                query = query.ToLower();
                search = search.Where(p => p.Id.ToLower().Contains(query));
            }

            var results = await search.Where(p => p.Listed)
                .OrderByDescending(p => p.Downloads)
                .Distinct()
                .Skip(skip)
                .Take(take)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);

            return new AutocompleteResponse
            {
                TotalHits = results.Count,
                Data = results,
                Context = AutocompleteContext.Default
            };
        }

        public async Task<DependentsResponse> FindDependentsAsync(
            string packageId,
            int skip = 0,
            int take = 20,
            CancellationToken cancellationToken = default)
        {
            var results = await _context
                .Packages
                .Where(p => p.Listed)
                .OrderByDescending(p => p.Downloads)
                .Where(p => p.Dependencies.Any(d => d.Id == packageId))
                .Skip(skip)
                .Take(take)
                .Select(p => p.Id)
                .Distinct()
                .ToListAsync(cancellationToken);

            return new DependentsResponse
            {
                TotalHits = results.Count,
                Data = results
            };
        }

        private async Task<List<IGrouping<string, Package>>> SearchImplAsync(
            string query,
            int skip,
            int take,
            bool includePrerelease,
            bool includeSemVer2,
            string packageType,
            string framework,
            CancellationToken cancellationToken)
        {
            var frameworks = GetCompatibleFrameworksOrNull(framework);
            IQueryable<Package> search = _context.Packages;

            IQueryable<Package> AddSearchFilters(IQueryable<Package> packageQuery)
            {
                if (!includePrerelease)
                {
                    packageQuery = packageQuery.Where(p => !p.IsPrerelease);
                }

                if (!includeSemVer2)
                {
                    packageQuery = packageQuery.Where(p => p.SemVerLevel != SemVerLevel.SemVer2);
                }

                if (!string.IsNullOrEmpty(packageType))
                {
                    packageQuery = packageQuery.Where(p => p.PackageTypes.Any(t => t.Name == packageType));
                }

                if (frameworks != null)
                {
                    packageQuery = packageQuery.Where(p => p.TargetFrameworks.Any(f => frameworks.Contains(f.Moniker)));
                }

                packageQuery = packageQuery.Where(p => p.Listed);

                return packageQuery;
            }

            search = AddSearchFilters(search);

            if (!string.IsNullOrEmpty(query))
            {
                query = query.ToLower();
                search = search.Where(p => p.Id.ToLower().Contains(query));
            }

            var packageIds = search.Select(p => p.Id)
                .Distinct()
                .OrderBy(id => id)
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
                var packageIdResults = await packageIds.ToListAsync(cancellationToken);

                search = _context.Packages.Where(p => packageIdResults.Contains(p.Id));
            }

            search = AddSearchFilters(search);

            var results = await search.ToListAsync(cancellationToken);

            return results.GroupBy(p => p.Id).ToList();
        }

        private IReadOnlyList<string> GetCompatibleFrameworksOrNull(string framework)
        {
            if (framework == null) return null;

            return _frameworks.FindAllCompatibleFrameworks(framework);
        }
    }
}
