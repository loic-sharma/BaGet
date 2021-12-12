using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Core
{
    /// <summary>
    /// The service that combines the state of indexed packages and
    /// upstream packages.
    /// For upstream packages, see <see cref="IUpstreamClient"/>.
    /// For indexed packages, see <see cref="IPackageDatabase"/>.
    /// </summary>
    public interface IPackageService
    {
        /// <summary>
        /// Attempt to find a package's versions using mirroring. This will merge
        /// results from the configured upstream source with the locally indexed packages.
        /// </summary>
        /// <param name="id">The package's id to lookup</param>
        /// <param name="cancellationToken">The token to cancel the lookup</param>
        /// <returns>
        /// The package's versions, or an empty list if the package cannot be found.
        /// This includes unlisted versions.
        /// </returns>
        Task<IReadOnlyList<NuGetVersion>> FindPackageVersionsAsync(string id, CancellationToken cancellationToken);

        /// <summary>
        /// Attempt to find a package's metadata using mirroring. This will merge
        /// results from the configured upstream source with the locally indexed packages.
        /// </summary>
        /// <param name="id">The package's id to lookup</param>
        /// <param name="cancellationToken">The token to cancel the lookup</param>
        /// <returns>
        /// The metadata for all versions of a package, including unlisted versions.
        /// Returns an empty list if the package cannot be found.
        /// </returns>
        Task<IReadOnlyList<Package>> FindPackagesAsync(string id, CancellationToken cancellationToken);

        /// <summary>
        /// Attempt to find a package's metadata using mirroring. This will merge
        /// results from the configured upstream source with the locally indexed packages.
        /// </summary>
        /// <param name="id">The package's id to lookup</param>
        /// <param name="version">The package's version to lookup</param>
        /// <param name="cancellationToken">The token to cancel the lookup</param>
        /// <returns>
        /// The metadata for single version of a package.
        /// Returns null if the package does not exist.
        /// </returns>
        Task<Package> FindPackageOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken);

        /// <summary>
        /// Determine whether a package exists locally or on the upstream source.
        /// </summary>
        /// <param name="id">The package id to search.</param>
        /// <param name="version">The package version to search.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>Whether the package exists in the database.</returns>
        Task<bool> ExistsAsync(string id, NuGetVersion version, CancellationToken cancellationToken);

        /// <summary>
        /// Increment a package's download count.
        /// </summary>
        /// <param name="packageId">The id of the package to update.</param>
        /// <param name="version">The id of the package to update.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        Task AddDownloadAsync(string packageId, NuGetVersion version, CancellationToken cancellationToken);
    }
}
