using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;
using NuGet.Versioning;

namespace BaGet.Core
{
    /// <summary>
    /// The Package Content resource, used to download NuGet packages and to fetch other metadata.
    ///
    /// See: https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource
    /// </summary>
    public interface IPackageContentService
    {
        /// <summary>
        /// Get a package's versions, or null if the package does not exist.
        /// See: https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource#enumerate-package-versions
        /// </summary>
        /// <param name="packageId">The package ID.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The package's versions, or null if the package does not exist.</returns>
        Task<PackageVersionsResponse> GetPackageVersionsOrNullAsync(
            string packageId,
            CancellationToken cancellationToken);

        /// <summary>
        /// Download a package, or null if the package does not exist.
        /// See: https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource#download-package-content-nupkg
        /// </summary>
        /// <param name="packageId">The package ID.</param>
        /// <param name="packageVersion">The package's version.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>
        /// The package's content stream, or null if the package does not exist. The stream may not be seekable.
        /// </returns>
        Task<Stream> GetPackageContentStreamOrNullAsync(
            string packageId,
            NuGetVersion packageVersion,
            CancellationToken cancellationToken);

        /// <summary>
        /// Download a package's manifest (nuspec), or null if the package does not exist.
        /// See: https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource#download-package-manifest-nuspec
        /// </summary>
        /// <param name="packageId">The package id.</param>
        /// <param name="packageVersion">The package's version.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>
        /// The package's manifest stream, or null if the package does not exist. The stream may not be seekable.
        /// </returns>
        Task<Stream> GetPackageManifestStreamOrNullAsync(
            string packageId,
            NuGetVersion packageVersion,
            CancellationToken cancellationToken);

        /// <summary>
        /// Download a package's readme, or null if the package or readme does not exist.
        /// </summary>
        /// <param name="id">The package id.</param>
        /// <param name="version">The package's version.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>
        /// The package's readme stream, or null if the package or readme does not exist. The stream may not be seekable.
        /// </returns>
        Task<Stream> GetPackageReadmeStreamOrNullAsync(
            string id,
            NuGetVersion version,
            CancellationToken cancellationToken);

        /// <summary>
        /// Download a package's icon, or null if the package or icon does not exist.
        /// </summary>
        /// <param name="id">The package id.</param>
        /// <param name="version">The package's version.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>
        /// The package's icon stream, or null if the package or icon does not exist. The stream may not be seekable.
        /// </returns>
        Task<Stream> GetPackageIconStreamOrNullAsync(
            string id,
            NuGetVersion version,
            CancellationToken cancellationToken);
    }
}
