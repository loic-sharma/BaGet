using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;

namespace BaGet.Core
{
    /// <summary>
    /// A minimal search service implementation, used for advanced scenarios.
    /// </summary>
    public class NullSearchService : ISearchService
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
            string query = null,
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

        public Task<SearchResponse> SearchAsync(
            string query = null,
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
            string query = null,
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
