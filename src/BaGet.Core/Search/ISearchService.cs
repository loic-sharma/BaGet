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
        /// <param name="query">The search query.</param>
        /// <param name="skip">How many results to skip.</param>
        /// <param name="take">How many results to return.</param>
        /// <param name="includePrerelease">Whether pre-release packages should be returned.</param>
        /// <param name="includeSemVer2">Whether packages that require SemVer 2.0.0 compatibility should be returned.</param>
        /// <param name="packageType">The type of packages that should be returned.</param>
        /// <param name="framework">The Target Framework that results should be compatible.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The search response.</returns>
        Task<SearchResponse> SearchAsync(
            string query,
            int skip,
            int take,
            bool includePrerelease,
            bool includeSemVer2,
            string packageType,
            string framework,
            CancellationToken cancellationToken);

        /// <summary>
        /// Perform an autocomplete query.
        /// See: https://docs.microsoft.com/en-us/nuget/api/search-autocomplete-service-resource#search-for-package-ids
        /// </summary>
        /// <param name="query">The autocomplete query.</param>
        /// <param name="skip">How many results to skip.</param>
        /// <param name="take">How many results to return.</param>
        /// <param name="includePrerelease">Whether pre-release packages should be returned.</param>
        /// <param name="includeSemVer2">Whether packages that require SemVer 2.0.0 compatibility should be returned.</param>
        /// <param name="packageType">The type of packages that should be returned.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The autocomplete response.</returns>
        Task<AutocompleteResponse> AutocompleteAsync(
            string query,
            int skip,
            int take,
            bool includePrerelease,
            bool includeSemVer2,
            string packageType,
            CancellationToken cancellationToken);

        /// <summary>
        /// Enumerate listed package versions.
        /// See: https://docs.microsoft.com/en-us/nuget/api/search-autocomplete-service-resource#enumerate-package-versions
        /// </summary>
        /// <param name="packageId">The package ID.</param>
        /// <param name="includePrerelease">Whether pre-release packages should be returned.</param>
        /// <param name="includeSemVer2">Whether packages that require SemVer 2.0.0 compatibility should be returned.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The package versions that matched the request.</returns>
        Task<AutocompleteResponse> ListPackageVersionsAssync(
            string packageId,
            bool includePrerelease,
            bool includeSemVer2,
            CancellationToken cancellationToken);

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
