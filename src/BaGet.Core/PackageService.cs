using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace BaGet.Core
{
    public class PackageService : IPackageService
    {
        private readonly IPackageDatabase _db;
        private readonly IUpstreamClient _upstream;
        private readonly IPackageIndexingService _indexer;
        private readonly ILogger<PackageService> _logger;

        public PackageService(
            IPackageDatabase db,
            IUpstreamClient upstream,
            IPackageIndexingService indexer,
            ILogger<PackageService> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
            _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<NuGetVersion>> FindPackageVersionsAsync(
            string id,
            CancellationToken cancellationToken)
        {
            var upstreamVersions = await _upstream.ListPackageVersionsAsync(id, cancellationToken);

            // Merge the local package versions into the upstream package versions.
            var localPackages = await _db.FindAsync(id, includeUnlisted: true, cancellationToken);
            var localVersions = localPackages.Select(p => p.Version);

            if (!upstreamVersions.Any()) return localVersions.ToList();
            if (!localPackages.Any()) return upstreamVersions;

            return upstreamVersions.Concat(localVersions).Distinct().ToList();
        }

        public async Task<IReadOnlyList<Package>> FindPackagesAsync(string id, CancellationToken cancellationToken)
        {
            var upstreamPackages = await _upstream.ListPackagesAsync(id, cancellationToken);
            var localPackages = await _db.FindAsync(id, includeUnlisted: true, cancellationToken);

            if (!upstreamPackages.Any()) return localPackages;
            if (!localPackages.Any()) return upstreamPackages;

            // Merge the local packages into the upstream packages.
            var result = upstreamPackages.ToDictionary(p => p.Version);
            var local = localPackages.ToDictionary(p => p.Version);

            foreach (var localPackage in local)
            {
                result[localPackage.Key] = localPackage.Value;
            }

            return result.Values.ToList();
        }

        public async Task<Package> FindPackageOrNullAsync(
            string id,
            NuGetVersion version,
            CancellationToken cancellationToken)
        {
            await MirrorAsync(id, version, cancellationToken);

            return await _db.FindOrNullAsync(id, version, includeUnlisted: true, cancellationToken);
        }

        public async Task<bool> ExistsAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        {
            await MirrorAsync(id, version, cancellationToken);

            return await _db.ExistsAsync(id, version, cancellationToken);
        }

        private async Task MirrorAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        {
            if (await _db.ExistsAsync(id, version, cancellationToken))
            {
                return;
            }

            _logger.LogInformation(
                "Package {PackageId} {PackageVersion} does not exist locally. Indexing from upstream feed...",
                id,
                version);

            try
            {
                using (var packageStream = await _upstream.DownloadPackageOrNullAsync(id, version, cancellationToken))
                {
                    if (packageStream == null)
                    {
                        _logger.LogWarning(
                            "Failed to download package {PackageId} {PackageVersion}",
                            id,
                            version);
                        return;
                    }

                    _logger.LogInformation(
                        "Downloaded package {PackageId} {PackageVersion}, indexing...",
                        id,
                        version);

                    var result = await _indexer.IndexAsync(packageStream, cancellationToken);

                    _logger.LogInformation(
                        "Finished indexing package {PackageId} {PackageVersion} from upstream feed with result {Result}",
                        id,
                        version,
                        result);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(
                    e,
                    "Failed to index package {PackageId} {PackageVersion} from upstream",
                    id,
                    version);
            }
        }
    }
}
