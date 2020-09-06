using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;

namespace BaGet.Core
{
    /// <summary>
    /// The service used to search for packages.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource
    /// </summary>
    public interface ISearchService
    {
        /// <summary>
        /// Perform a search query.
        /// See: https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource#search-for-packages
        /// </summary>
        /// <param name="request">The search request.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The search response.</returns>
        Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Perform an autocomplete query.
        /// See: https://docs.microsoft.com/en-us/nuget/api/search-autocomplete-service-resource#search-for-package-ids
        /// </summary>
        /// <param name="request">The autocomplete request.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The autocomplete response.</returns>
        Task<AutocompleteResponse> AutocompleteAsync(AutocompleteRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Enumerate listed package versions.
        /// See: https://docs.microsoft.com/en-us/nuget/api/search-autocomplete-service-resource#enumerate-package-versions
        /// </summary>
        /// <param name="request">The autocomplete request.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The package versions that matched the request.</returns>
        Task<AutocompleteResponse> ListPackageVersionsAsync(VersionsRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Find the packages that depend on a given package.
        /// </summary>
        /// <param name="packageId">The package whose dependents should be found.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The dependents response.</returns>
        Task<DependentsResponse> FindDependentsAsync(
            string packageId,
            CancellationToken cancellationToken);
    }
}
