using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using BaGet.Core.Indexing;
using BaGet.Core.ServiceIndex;
using BaGet.Protocol;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core.Search
{
    public class DatabaseSearchService : IBaGetSearchResource
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

        public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
        {
            return await SearchAsync(BaGetSearchRequest.FromSearchRequest(request), cancellationToken);
        }

        public async Task<SearchResponse> SearchAsync(BaGetSearchRequest request, CancellationToken cancellationToken)
        {
            var result = new List<SearchResult>();
            var packages = await SearchImplAsync(request, cancellationToken);

            foreach (var package in packages)
            {
                var versions = package.OrderByDescending(p => p.Version).ToList();
                var latest = versions.First();

                var versionResults = versions.Select(p => new SearchResultVersion(
                    registrationLeafUrl: _url.GetRegistrationLeafUrl(p.Id, p.Version),
                    p.Version,
                    p.Downloads));

                result.Add(new SearchResult(
                    latest.Id,
                    latest.Version,
                    latest.Description,
                    latest.Authors,
                    latest.IconUrlString,
                    latest.LicenseUrlString,
                    latest.ProjectUrlString,
                    registrationIndexUrl: _url.GetRegistrationIndexUrl(latest.Id),
                    latest.Summary,
                    latest.Tags,
                    latest.Title,
                    versions.Sum(p => p.Downloads),
                    versionResults.ToList()));
            }

            return new SearchResponse(
                result.Count,
                result,
                SearchContext.Default(_url.GetPackageMetadataResourceUrl()));
        }

        public async Task<AutocompleteResponse> AutocompleteAsync(AutocompleteRequest request, CancellationToken cancellationToken)
        {
            // TODO: Support versions autocomplete.
            // See: https://github.com/loic-sharma/BaGet/issues/291
            IQueryable<Package> search = _context.Packages;

            if (!string.IsNullOrEmpty(request.Query))
            {
                var query = request.Query.ToLower();
                search = search.Where(p => p.Id.ToLower().Contains(query));
            }

            var results = await search.Where(p => p.Listed)
                .OrderByDescending(p => p.Downloads)
                .Skip(request.Skip)
                .Take(request.Take)
                .Select(p => p.Id)
                .Distinct()
                .ToListAsync(cancellationToken);

            return new AutocompleteResponse(
                results.Count,
                results,
                AutocompleteContext.Default);
        }

        public async Task<DependentsResponse> FindDependentsAsync(DependentsRequest request, CancellationToken cancellationToken)
        {
            var results = await _context
                .Packages
                .Where(p => p.Listed)
                .OrderByDescending(p => p.Downloads)
                .Where(p => p.Dependencies.Any(d => d.Id == request.PackageId))
                .Skip(request.Skip)
                .Take(request.Take)
                .Select(p => p.Id)
                .Distinct()
                .ToListAsync(cancellationToken);

            return new DependentsResponse(results.Count, results);
        }

        private async Task<List<IGrouping<string, Package>>> SearchImplAsync(
            BaGetSearchRequest request,
            CancellationToken cancellationToken)
        {
            var frameworks = GetCompatibleFrameworksOrNull(request.Framework);
            var search = (IQueryable<Package>)_context.Packages.Where(p => p.Listed);

            if (!string.IsNullOrEmpty(request.Query))
            {
                var query = request.Query.ToLower();
                search = search.Where(p => p.Id.ToLower().Contains(query));
            }

            if (!request.IncludePrerelease)
            {
                search = search.Where(p => !p.IsPrerelease);
            }

            if (!request.IncludeSemVer2)
            {
                search = search.Where(p => p.SemVerLevel != SemVerLevel.SemVer2);
            }

            if (!string.IsNullOrEmpty(request.PackageType))
            {
                search = search.Where(p => p.PackageTypes.Any(t => t.Name == request.PackageType));
            }

            if (frameworks != null)
            {
                search = search.Where(p => p.TargetFrameworks.Any(f => frameworks.Contains(f.Moniker)));
            }

            var packageIds = search.Select(p => p.Id)
                .OrderBy(id => id)
                .Distinct()
                .Skip(request.Skip)
                .Take(request.Take);

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

            return await search.GroupBy(p => p.Id).ToListAsync(cancellationToken);
        }

        private IReadOnlyList<string> GetCompatibleFrameworksOrNull(string framework)
        {
            if (framework == null) return null;

            return _frameworks.FindAllCompatibleFrameworks(framework);
        }
    }
}
