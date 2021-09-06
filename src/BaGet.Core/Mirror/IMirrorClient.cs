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
    public interface IMirrorClient
    {
        /// <summary>
        /// Try to get the metadata for all versions of a package from the upstream package source. Returns empty
        /// if the package could not be found.
        /// </summary>
        /// <param name="id">The package ID to lookup</param>
        /// <param name="cancellationToken"></param>
        /// <returns>
        /// The metadata for all versions of a package, including unlisted packages.
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
        Task<IReadOnlyList<PackageMetadata>> GetPackageMetadataAsync(string id, CancellationToken cancellationToken);

        /// <summary>
        /// Download a package from the upstream package source. Throws if the package does not exist.
        /// </summary>
        /// <param name="id">The package ID to download.</param>
        /// <param name="version">The package version to download.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The package stream. May not be buffered or seekable.</returns>
        Task<Stream> DownloadPackageAsync(string id, NuGetVersion version, CancellationToken cancellationToken);
    }
}
