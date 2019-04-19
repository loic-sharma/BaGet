using System.Collections.Generic;
using System.Threading.Tasks;
using BaGet.Core.Entities;

namespace BaGet.Core.Services
{
    /// <summary>
    /// A minimal search service implementation, used for advanced scenarios.
    /// </summary>
    public class NullSearchService : ISearchService
    {
        private static readonly Task<IReadOnlyList<string>> EmptyStringListTask
            = Task.FromResult((IReadOnlyList<string>)new List<string>());

        private static readonly Task<IReadOnlyList<SearchResult>> EmptySearchResultListTask
            = Task.FromResult((IReadOnlyList<SearchResult>)new List<SearchResult>());

        public Task<IReadOnlyList<string>> AutocompleteAsync(string query, int skip = 0, int take = 20)
        {
            return EmptyStringListTask;
        }

        public Task<IReadOnlyList<string>> FindDependentsAsync(string packageId, int skip = 0, int take = 20)
        {
            return EmptyStringListTask;
        }

        public Task IndexAsync(Package package)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SearchResult>> SearchAsync(
            string query,
            int skip = 0,
            int take = 20,
            bool includePrerelease = true,
            bool includeSemVer2 = true,
            string packageType = null,
            string framework = null)
        {
            return EmptySearchResultListTask;
        }
    }
}
