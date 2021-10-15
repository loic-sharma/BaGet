using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Core
{
    /// <summary>
    /// Indexes packages from an external source.
    /// </summary>
    public interface IMirrorService
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
        /// If the package is unknown, attempt to index it from an upstream source.
        /// </summary>
        /// <param name="id">The package's id</param>
        /// <param name="version">The package's version</param>
        /// <param name="cancellationToken">The token to cancel the mirroring</param>
        /// <returns>A task that completes when the package has been mirrored.</returns>
        Task MirrorAsync(string id, NuGetVersion version, CancellationToken cancellationToken);
    }
}
