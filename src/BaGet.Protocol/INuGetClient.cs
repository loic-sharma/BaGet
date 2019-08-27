using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    /// <summary>
    /// A NuGet client that interact with a NuGet server.
    /// </summary>
    public interface INuGetClient
    {
        /// <summary>
        /// Download a package (.nupkg), or throws if the package does not exist.
        /// </summary>
        /// <param name="packageId">The package ID.</param>
        /// <param name="packageVersion">The package's version.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The package's content stream. The stream may not be seekable.</returns>
        /// <exception cref="PackageNotFoundException">
        ///     The package could not be found.
        /// </exception>
        Task<Stream> GetPackageStreamAsync(
            string packageId,
            NuGetVersion packageVersion,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Download a package's manifest (.nuspec), or throws if the package does not exist.
        /// </summary>
        /// <param name="packageId">The package ID.</param>
        /// <param name="packageVersion">The package's version.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The package's manifest stream. The stream may not be seekable.</returns>
        /// <exception cref="PackageNotFoundException">
        ///     The package could not be found.
        /// </exception>
        Task<Stream> GetPackageManifestStreamAsync(
            string packageId,
            NuGetVersion packageVersion,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Find all versions of a package, excluding unlisted versions.
        /// </summary>
        /// <param name="packageId">The package ID.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The package's listed versions, or an empty list if the package does not exist.</returns>
        Task<IReadOnlyList<NuGetVersion>> ListPackageVersions(
            string packageId,
            CancellationToken cancellationToken);

        /// <summary>
        /// Find all versions of a package.
        /// </summary>
        /// <param name="packageId">The package ID.</param>
        /// <param name="includeUnlisted">Whether to include unlisted versions.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The package's versions, or an empty list if the package does not exist.</returns>
        Task<IReadOnlyList<NuGetVersion>> ListPackageVersions(
            string packageId,
            bool includeUnlisted,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Find the metadata for all versions of a package.
        /// </summary>
        /// <param name="packageId">The package ID.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The package's metadata, or an empty list if the package does not exist.</returns>
        Task<IReadOnlyList<RegistrationIndexPageItem>> GetPackageMetadataAsync(
            string packageId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Find the metadata for a single version of a package, or throws if the package does not exist.
        /// </summary>
        /// <param name="packageId">The package ID.</param>
        /// <param name="packageVersion">The package version.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The package's metadata.</returns>
        /// <exception cref="PackageNotFoundException">
        ///     The package could not be found.
        /// </exception>
        Task<RegistrationIndexPageItem> GetPackageMetadataAsync(
            string packageId,
            NuGetVersion packageVersion,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Search for packages. Includes prerelease packages.
        /// </summary>
        /// <param name="query">The search query.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The search results, including prerelease packages.</returns>
        Task<SearchResponse> SearchAsync(
            string query,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Search for packages.
        /// </summary>
        /// <param name="query">The search query.</param>
        /// <param name="includePrerelease">Whether to include prerelease packages.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The search results.</returns>
        Task<SearchResponse> SearchAsync(
            string query,
            bool includePrerelease,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Search for packages. Includes prerelease packages.
        /// </summary>
        /// <param name="query">The search query.</param>
        /// <param name="skip">The number of results to skip.</param>
        /// <param name="take">The number of results to include.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The search results, including prerelease packages.</returns>
        Task<SearchResponse> SearchAsync(
            string query,
            int skip,
            int take,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Search for packages.
        /// </summary>
        /// <param name="query">The search query.</param>
        /// <param name="includePrerelease">Whether to include prerelease packages.</param>
        /// <param name="skip">The number of results to skip.</param>
        /// <param name="take">The number of results to include.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The search results, including prerelease packages.</returns>
        Task<SearchResponse> SearchAsync(
            string query,
            bool includePrerelease,
            int skip,
            int take,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Search for package IDs.
        /// </summary>
        /// <param name="query">The search query.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The autocomplete results.</returns>
        Task<AutocompleteResponse> AutocompleteAsync(
            string query,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Search for package IDs.
        /// </summary>
        /// <param name="query">The search query.</param>
        /// <param name="skip">The number of results to skip.</param>
        /// <param name="take">The number of results to include.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The autocomplete results.</returns>
        Task<AutocompleteResponse> AutocompleteAsync(
            string query,
            int skip,
            int take,
            CancellationToken cancellationToken = default);


    }
}
