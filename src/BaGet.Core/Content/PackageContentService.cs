using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;
using NuGet.Versioning;

namespace BaGet.Core.Content
{
    /// <summary>
    /// Implements the NuGet Package Content resource. Supports read-through caching.
    /// Tracks state in a database (<see cref="IPackageService"/>) and stores packages
    /// using <see cref="IPackageStorageService"/>.
    /// </summary>
    public class PackageContentService
    {
        private readonly IMirrorService _mirror;
        private readonly IPackageService _packages;
        private readonly PackageStorageService _storage;

        public PackageContentService(
            IMirrorService mirror,
            IPackageService packages,
            PackageStorageService storage)
        {
            _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
            _packages = packages ?? throw new ArgumentNullException(nameof(packages));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }
        /// <summary>
        /// Get a package's versions, or null if the package does not exist.
        /// See: https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource#enumerate-package-versions
        /// </summary>
        /// <param name="packageId">The package ID.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The package's versions, or null if the package does not exist.</returns>
        public virtual async Task<PackageVersionsResponse> GetPackageVersionsOrNullAsync(
            string packageId,
            CancellationToken cancellationToken = default)
        {
            // First, attempt to find all package versions using the upstream source.
            var versions = await _mirror.FindPackageVersionsOrNullAsync(packageId, cancellationToken);

            if (versions == null)
            {
                // Fallback to the local packages if mirroring is disabled.
                var packages = await _packages.FindAsync(packageId, includeUnlisted: true, cancellationToken);

                if (!packages.Any())
                {
                    return null;
                }

                versions = packages.Select(p => p.Version).ToList();
            }

            return new PackageVersionsResponse
            {
                Versions = versions
                    .Select(v => v.ToNormalizedString())
                    .Select(v => v.ToLowerInvariant())
                    .ToList()
            };
        }
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
        public virtual async Task<Stream> GetPackageContentStreamOrNullAsync(
            string packageId,
            NuGetVersion packageVersion,
            CancellationToken cancellationToken = default)
        {
            // Allow read-through caching if it is configured.
            await _mirror.MirrorAsync(packageId, packageVersion, cancellationToken);

            if (!await _packages.AddDownloadAsync(packageId, packageVersion, cancellationToken))
            {
                return null;
            }

            return await _storage.GetPackageStreamAsync(packageId, packageVersion, cancellationToken);
        }
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
        public virtual async Task<Stream> GetPackageManifestStreamOrNullAsync(string packageId, NuGetVersion packageVersion, CancellationToken cancellationToken = default)
        {
            // Allow read-through caching if it is configured.
            await _mirror.MirrorAsync(packageId, packageVersion, cancellationToken);

            if (!await _packages.ExistsAsync(packageId, packageVersion, cancellationToken))
            {
                return null;
            }

            return await _storage.GetNuspecStreamAsync(packageId, packageVersion, cancellationToken);
        }
        /// <summary>
        /// Download a package's readme, or null if the package or readme does not exist.
        /// </summary>
        /// <param name="packageId">The package id.</param>
        /// <param name="packageVersion">The package's version.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>
        /// The package's readme stream, or null if the package or readme does not exist. The stream may not be seekable.
        /// </returns>
        public virtual async Task<Stream> GetPackageReadmeStreamOrNullAsync(string packageId, NuGetVersion packageVersion, CancellationToken cancellationToken = default)
        {
            // Allow read-through caching if it is configured.
            await _mirror.MirrorAsync(packageId, packageVersion, cancellationToken);

            var package = await _packages.FindOrNullAsync(packageId, packageVersion, includeUnlisted: true, cancellationToken);
            if (!package.HasReadme)
            {
                return null;
            }

            return await _storage.GetReadmeStreamAsync(packageId, packageVersion, cancellationToken);
        }
    }
}
