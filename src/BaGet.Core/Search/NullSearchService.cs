using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using BaGet.Protocol;

namespace BaGet.Core.Search
{
    /// <summary>
    /// A minimal search service implementation, used for advanced scenarios.
    /// </summary>
    public class NullSearchService : IBaGetSearchService
    {
        private static readonly IReadOnlyList<string> EmptyStringList = new List<string>();

        private static readonly Task<AutocompleteResponse> EmptyAutocompleteResponseTask =
            Task.FromResult(new AutocompleteResponse(0, EmptyStringList, AutocompleteContext.Default));

        private static readonly Task<DependentsResponse> EmptyDependentsResponseTask =
            Task.FromResult(new DependentsResponse(0, EmptyStringList));

        private static readonly Task<SearchResponse> EmptySearchResponseTask =
            Task.FromResult(new SearchResponse(0, new List<SearchResult>()));

        public Task<AutocompleteResponse> AutocompleteAsync(AutocompleteRequest request, CancellationToken cancellationToken = default)
        {
            return EmptyAutocompleteResponseTask;
        }

        public Task<DependentsResponse> FindDependentsAsync(DependentsRequest request, CancellationToken cancellationToken = default)
        {
            return EmptyDependentsResponseTask;
        }

        public Task IndexAsync(Package package, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<SearchResponse> SearchAsync(BaGetSearchRequest request, CancellationToken cancellationToken = default)
        {
            return EmptySearchResponseTask;
        }

        public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
        {
            return EmptySearchResponseTask;
        }
    }
}
