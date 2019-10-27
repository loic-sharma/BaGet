using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace BaGet.Core
{
    /// <summary>
    /// Stores packages' content. Packages' state are stored by the
    /// <see cref="IPackageService"/>.
    /// </summary>
    public class PackageStorageService
    {
        private const string PackagesPathPrefix = "packages";

        // See: https://github.com/NuGet/NuGetGallery/blob/73a5c54629056b25b3a59960373e8fef88abff36/src/NuGetGallery.Core/CoreConstants.cs#L19
        private const string PackageContentType = "binary/octet-stream";
        private const string NuspecContentType = "text/plain";
        private const string ReadmeContentType = "text/markdown";

        private readonly IStorageService _storage;
        private readonly ILogger<PackageStorageService> _logger;

        public PackageStorageService(
            IStorageService storage,
            ILogger<PackageStorageService> logger)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Persist a package's content to storage. This operation MUST fail if a package
        /// with the same id/version but different content has already been stored.
        /// </summary>
        /// <param name="package">The package's metadata.</param>
        /// <param name="packageStream">The package's nupkg stream.</param>
        /// <param name="nuspecStream">The package's nuspec stream.</param>
        /// <param name="readmeStream">The package's readme stream, or null if none.</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public virtual async Task SavePackageContentAsync(
            Package package,
            Stream packageStream,
            Stream nuspecStream,
            Stream readmeStream,
            CancellationToken cancellationToken = default)
        {
            package = package ?? throw new ArgumentNullException(nameof(package));
            packageStream = packageStream ?? throw new ArgumentNullException(nameof(packageStream));
            nuspecStream = nuspecStream ?? throw new ArgumentNullException(nameof(nuspecStream));

            var lowercasedId = package.Id.ToLowerInvariant();
            var lowercasedNormalizedVersion = package.NormalizedVersionString.ToLowerInvariant();

            var packagePath = PackagePath(lowercasedId, lowercasedNormalizedVersion);
            var nuspecPath = NuspecPath(lowercasedId, lowercasedNormalizedVersion);
            var readmePath = ReadmePath(lowercasedId, lowercasedNormalizedVersion);

            _logger.LogInformation(
                "Storing package {PackageId} {PackageVersion} at {Path}...",
                lowercasedId,
                lowercasedNormalizedVersion,
                packagePath);

            // Store the package.
            var result = await _storage.PutAsync(packagePath, packageStream, PackageContentType, cancellationToken);
            if (result == StoragePutResult.Conflict)
            {
                // TODO: This should be returned gracefully with an enum.
                _logger.LogInformation(
                    "Could not store package {PackageId} {PackageVersion} at {Path} due to conflict",
                    lowercasedId,
                    lowercasedNormalizedVersion,
                    packagePath);

                throw new InvalidOperationException($"Failed to store package {lowercasedId} {lowercasedNormalizedVersion}");
            }

            // Store the package's nuspec.
            _logger.LogInformation(
                "Storing package {PackageId} {PackageVersion} nuspec at {Path}...",
                lowercasedId,
                lowercasedNormalizedVersion,
                nuspecPath);

            result = await _storage.PutAsync(nuspecPath, nuspecStream, NuspecContentType, cancellationToken);
            if (result == StoragePutResult.Conflict)
            {
                // TODO: This should be returned gracefully with an enum.
                _logger.LogInformation(
                    "Could not store package {PackageId} {PackageVersion} nuspec at {Path} due to conflict",
                    lowercasedId,
                    lowercasedNormalizedVersion,
                    nuspecPath);

                throw new InvalidOperationException($"Failed to store package {lowercasedId} {lowercasedNormalizedVersion} nuspec");
            }

            // Store the package's readme, if one exists.
            if (readmeStream != null)
            {
                _logger.LogInformation(
                    "Storing package {PackageId} {PackageVersion} readme at {Path}...",
                    lowercasedId,
                    lowercasedNormalizedVersion,
                    readmePath);

                result = await _storage.PutAsync(readmePath, readmeStream, ReadmeContentType, cancellationToken);
                if (result == StoragePutResult.Conflict)
                {
                    // TODO: This should be returned gracefully with an enum.
                    _logger.LogInformation(
                        "Could not store package {PackageId} {PackageVersion} readme at {Path} due to conflict",
                        lowercasedId,
                        lowercasedNormalizedVersion,
                        readmePath);

                    throw new InvalidOperationException($"Failed to store package {lowercasedId} {lowercasedNormalizedVersion} readme");
                }
            }

            _logger.LogInformation(
                "Finished storing package {PackageId} {PackageVersion}",
                lowercasedId,
                lowercasedNormalizedVersion);
        }

        /// <summary>
        /// Retrieve a package's nupkg stream.
        /// </summary>
        /// <param name="id">The package's id.</param>
        /// <param name="version">The package's version.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The package's nupkg stream.</returns>
        public virtual async Task<Stream> GetPackageStreamAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        {
            return await GetStreamAsync(id, version, PackagePath, cancellationToken);
        }

        /// <summary>
        /// Retrieve a package's nuspec stream.
        /// </summary>
        /// <param name="id">The package's id.</param>
        /// <param name="version">The package's version.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The package's nuspec stream.</returns>
        public virtual async Task<Stream> GetNuspecStreamAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        {
            return await GetStreamAsync(id, version, NuspecPath, cancellationToken);
        }

        /// <summary>
        /// Retrieve a package's readme stream.
        /// </summary>
        /// <param name="id">The package's id.</param>
        /// <param name="version">The package's version.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The package's readme stream.</returns>
        public virtual async Task<Stream> GetReadmeStreamAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        {
            return await GetStreamAsync(id, version, ReadmePath, cancellationToken);
        }

        /// <summary>
        /// Remove a package's content from storage. This operation SHOULD succeed
        /// even if the package does not exist.
        /// </summary>
        /// <param name="id">The package's id.</param>
        /// <param name="version">The package's version.</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public virtual async Task DeleteAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        {
            var lowercasedId = id.ToLowerInvariant();
            var lowercasedNormalizedVersion = version.ToNormalizedString().ToLowerInvariant();

            var packagePath = PackagePath(lowercasedId, lowercasedNormalizedVersion);
            var nuspecPath = NuspecPath(lowercasedId, lowercasedNormalizedVersion);
            var readmePath = ReadmePath(lowercasedId, lowercasedNormalizedVersion);

            await _storage.DeleteAsync(packagePath, cancellationToken);
            await _storage.DeleteAsync(nuspecPath, cancellationToken);
            await _storage.DeleteAsync(readmePath, cancellationToken);
        }

        private async Task<Stream> GetStreamAsync(
            string id,
            NuGetVersion version,
            Func<string, string, string> pathFunc,
            CancellationToken cancellationToken)
        {
            var lowercasedId = id.ToLowerInvariant();
            var lowercasedNormalizedVersion = version.ToNormalizedString().ToLowerInvariant();
            var path = pathFunc(lowercasedId, lowercasedNormalizedVersion);

            try
            {
                return await _storage.GetAsync(path, cancellationToken);
            }
            catch (DirectoryNotFoundException)
            {
                // The "packages" prefix was lowercased, which was a breaking change
                // on filesystems that are case sensitive. Handle this case to help
                // users migrate to the latest version of BaGet.
                // See https://github.com/loic-sharma/BaGet/issues/298
                _logger.LogError(
                    $"Unable to find the '{PackagesPathPrefix}' folder. " +
                    "If you've recently upgraded BaGet, please make sure this folder starts with a lowercased letter. " +
                    "For more information, please see https://github.com/loic-sharma/BaGet/issues/298");
                throw;
            }
        }

        private string PackagePath(string lowercasedId, string lowercasedNormalizedVersion)
        {
            return Path.Combine(
                PackagesPathPrefix,
                lowercasedId,
                lowercasedNormalizedVersion,
                $"{lowercasedId}.{lowercasedNormalizedVersion}.nupkg");
        }

        private string NuspecPath(string lowercasedId, string lowercasedNormalizedVersion)
        {
            return Path.Combine(
                PackagesPathPrefix,
                lowercasedId,
                lowercasedNormalizedVersion,
                $"{lowercasedId}.nuspec");
        }

        private string ReadmePath(string lowercasedId, string lowercasedNormalizedVersion)
        {
            return Path.Combine(
                PackagesPathPrefix,
                lowercasedId,
                lowercasedNormalizedVersion,
                "readme");
        }
    }
}
