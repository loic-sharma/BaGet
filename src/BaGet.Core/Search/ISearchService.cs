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
        /// Add a package to the search index.
        /// </summary>
        /// <param name="package">The package to add.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>A task that completes once the package has been added.</returns>
        Task IndexAsync(Package package, CancellationToken cancellationToken = default);

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
            string query = null,
            int skip = 0,
            int take = 20,
            bool includePrerelease = true,
            bool includeSemVer2 = true,
            string packageType = null,
            string framework = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Perform an autocomplete query.
        /// See: https://docs.microsoft.com/en-us/nuget/api/search-autocomplete-service-resource
        /// </summary>
        /// <param name="query">The autocomplete query.</param>
        /// <param name="type">The autocomplete request type.</param>
        /// <param name="skip">How many results to skip.</param>
        /// <param name="take">How many results to return.</param>
        /// <param name="includePrerelease">Whether pre-release packages should be returned.</param>
        /// <param name="includeSemVer2">Whether packages that require SemVer 2.0.0 compatibility should be returned.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The autocomplete response.</returns>
        Task<AutocompleteResponse> AutocompleteAsync(
            string query = null,
            AutocompleteType type = AutocompleteType.PackageIds,
            int skip = 0,
            int take = 20,
            bool includePrerelease = true,
            bool includeSemVer2 = true,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Find the packages that depend on a given package.
        /// </summary>
        /// <param name="packageId">The package whose dependents should be found.</param>
        /// <param name="skip">How many results to skip.</param>
        /// <param name="take">How many results to return.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The dependents response.</returns>
        Task<DependentsResponse> FindDependentsAsync(
            string packageId,
            int skip = 0,
            int take = 20,
            CancellationToken cancellationToken = default);
    }
}
