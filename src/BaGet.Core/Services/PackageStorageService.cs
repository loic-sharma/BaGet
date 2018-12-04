using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace BaGet.Core.Services
{
    public class PackageStorageService : IPackageStorageService
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

        public async Task SavePackageContentAsync(
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
            var lowercasedNormalizedVersion = package.VersionString.ToLowerInvariant();

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
            if (result == PutResult.Conflict)
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
            if (result == PutResult.Conflict)
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
                if (result == PutResult.Conflict)
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

        public async Task<Stream> GetPackageStreamAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        {
            return await GetStreamAsync(id, version, PackagePath, cancellationToken);
        }

        public async Task<Stream> GetNuspecStreamAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        {
            return await GetStreamAsync(id, version, NuspecPath, cancellationToken);
        }

        public async Task<Stream> GetReadmeStreamAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        {
            return await GetStreamAsync(id, version, ReadmePath, cancellationToken);
        }

        public async Task DeleteAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
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

            return await _storage.GetAsync(path, cancellationToken);
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
