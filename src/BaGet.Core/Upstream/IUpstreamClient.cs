using BaGet.Protocol.Models;
using NuGet.Versioning;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core
{
    /// <summary>
    /// A client to interact with an upstream package source.
    /// </summary>
    public interface IUpstreamClient
    {
        /// <summary>
        /// Try to get all versions of a package from the upstream package source. Returns empty
        /// if the package could not be found.
        /// </summary>
        /// <param name="id">The package ID to lookup</param>
        /// <param name="cancellationToken"></param>
        /// <returns>
        /// All versions of a package, including unlisted packages.
        /// Returns an empty list if the package could not be found.
        /// </returns>
        Task<IReadOnlyList<NuGetVersion>> ListPackageVersionsAsync(string id, CancellationToken cancellationToken);

        /// <summary>
        /// Try to get the metadata for all versions of a package from the upstream package source. Returns empty
        /// if the package could not be found.
        /// </summary>
        /// <param name="id">The package ID to lookup</param>
        /// <param name="cancellationToken"></param>
        /// <returns>
        /// The metadata for all versions of a package, including unlisted versions.
        /// Returns an empty list if the package could not be found.
        /// </returns>
        Task<IReadOnlyList<Package>> ListPackagesAsync(string id, CancellationToken cancellationToken);

        /// <summary>
        /// Download a package from the upstream package source. Returns null if the package does not exist.
        /// </summary>
        /// <param name="id">The package ID to download.</param>
        /// <param name="version">The package version to download.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>
        /// The package stream or null if the package cannot be found.
        /// The stream is guaranteed to be seekable if not not null.
        /// </returns>
        Task<Stream> DownloadPackageOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken);
    }
}
