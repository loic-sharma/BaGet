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
        private readonly IPackageStorageService _storage;

        public PackageContentService(
            IMirrorService mirror,
            IPackageService packages,
            IPackageStorageService storage)
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
            string id,
            CancellationToken cancellationToken = default)
        {
            // First, attempt to find all package versions using the upstream source.
            var versions = await _mirror.FindPackageVersionsOrNullAsync(id, cancellationToken);

            if (versions == null)
            {
                // Fallback to the local packages if mirroring is disabled.
                var packages = await _packages.FindAsync(id, includeUnlisted: true, cancellationToken);

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
            string id,
            NuGetVersion version,
            CancellationToken cancellationToken = default)
        {
            // Allow read-through caching if it is configured.
            await _mirror.MirrorAsync(id, version, cancellationToken);

            if (!await _packages.AddDownloadAsync(id, version, cancellationToken))
            {
                return null;
            }

            return await _storage.GetPackageStreamAsync(id, version, cancellationToken);
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
        public virtual async Task<Stream> GetPackageManifestStreamOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken = default)
        {
            // Allow read-through caching if it is configured.
            await _mirror.MirrorAsync(id, version, cancellationToken);

            if (!await _packages.ExistsAsync(id, version, cancellationToken))
            {
                return null;
            }

            return await _storage.GetNuspecStreamAsync(id, version, cancellationToken);
        }
        /// <summary>
        /// Download a package's readme, or null if the package or readme does not exist.
        /// </summary>
        /// <param name="id">The package id.</param>
        /// <param name="version">The package's version.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>
        /// The package's readme stream, or null if the package or readme does not exist. The stream may not be seekable.
        /// </returns>
        public virtual async Task<Stream> GetPackageReadmeStreamOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken = default)
        {
            // Allow read-through caching if it is configured.
            await _mirror.MirrorAsync(id, version, cancellationToken);

            var package = await _packages.FindOrNullAsync(id, version, includeUnlisted: true, cancellationToken);
            if (!package.HasReadme)
            {
                return null;
            }

            return await _storage.GetReadmeStreamAsync(id, version, cancellationToken);
        }
    }
}
