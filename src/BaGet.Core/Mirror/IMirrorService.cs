using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using NuGet.Versioning;

namespace BaGet.Core.Mirror
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
        /// The package's versions, or null if the package cannot be found on the
        /// configured upstream source. This includes unlisted versions.
        /// </returns>
        Task<IReadOnlyList<NuGetVersion>> FindPackageVersionsOrNullAsync(string id, CancellationToken cancellationToken);

        /// <summary>
        /// Attempt to find a package's metadata using mirroring. This will merge
        /// results from the configured upstream source with the locally indexed packages.
        /// </summary>
        /// <param name="id">The package's id to lookup</param>
        /// <param name="cancellationToken">The token to cancel the lookup</param>
        /// <returns>
        /// The package's metadata, or null if the package cannot be found on the configured
        /// upstream source.
        /// </returns>
        Task<IReadOnlyList<Package>> FindPackagesOrNullAsync(string id, CancellationToken cancellationToken);

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
