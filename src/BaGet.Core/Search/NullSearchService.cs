using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using BaGet.Protocol.Models;

namespace BaGet.Core.Search
{
    /// <summary>
    /// A minimal search service implementation, used for advanced scenarios.
    /// </summary>
    public class NullSearchService : IBaGetSearchResource
    {
        private static readonly IReadOnlyList<string> EmptyStringList = new List<string>();

        private static readonly Task<AutocompleteResponse> EmptyAutocompleteResponseTask =
            Task.FromResult(new AutocompleteResponse
            {
                TotalHits = 0,
                Data = EmptyStringList,
                Context = AutocompleteContext.Default
            });

        private static readonly Task<DependentsResponse> EmptyDependentsResponseTask =
            Task.FromResult(new DependentsResponse
            {
                TotalHits = 0,
                Data = EmptyStringList
            });

        private static readonly Task<SearchResponse> EmptySearchResponseTask =
            Task.FromResult(new SearchResponse
            {
                TotalHits = 0,
                Data = new List<SearchResult>()
            });

        public Task<AutocompleteResponse> AutocompleteAsync(
            string query,
            AutocompleteType type = AutocompleteType.PackageIds,
            int skip = 0,
            int take = 20,
            bool includePrerelease = true,
            bool includeSemVer2 = true,
            CancellationToken cancellationToken = default)
        {
            return EmptyAutocompleteResponseTask;
        }

        public Task<DependentsResponse> FindDependentsAsync(
            string packageId,
            int skip = 0,
            int take = 20,
            CancellationToken cancellationToken = default)
        {
            return EmptyDependentsResponseTask;
        }

        public Task IndexAsync(Package package, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<SearchResponse> SearchAsync(
            string query,
            int skip = 0,
            int take = 20,
            bool includePrerelease = true,
            bool includeSemVer2 = true,
            string packageType = null,
            string framework = null,
            CancellationToken cancellationToken = default)
        {
            return EmptySearchResponseTask;
        }

        public Task<SearchResponse> SearchAsync(
            string query,
            int skip = 0,
            int take = 20,
            bool includePrerelease = true,
            bool includeSemVer2 = true,
            CancellationToken cancellationToken = default)
        {
            return EmptySearchResponseTask;
        }
    }
}
