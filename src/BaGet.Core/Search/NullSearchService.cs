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
                Data = new List<PackageDependent>()
            });

        private static readonly Task<SearchResponse> EmptySearchResponseTask =
            Task.FromResult(new SearchResponse
            {
                TotalHits = 0,
                Data = new List<SearchResult>()
            });

        public Task<AutocompleteResponse> AutocompleteAsync(
            AutocompleteRequest request,
            CancellationToken cancellationToken)
        {
            return EmptyAutocompleteResponseTask;
        }

        public Task<AutocompleteResponse> ListPackageVersionsAsync(
            VersionsRequest request,
            CancellationToken cancellationToken)
        {
            return EmptyAutocompleteResponseTask;
        }

        public Task<DependentsResponse> FindDependentsAsync(string packageId, CancellationToken cancellationToken)
        {
            return EmptyDependentsResponseTask;
        }

        public Task<SearchResponse> SearchAsync(
            SearchRequest request,
            CancellationToken cancellationToken)
        {
            return EmptySearchResponseTask;
        }
    }
}
