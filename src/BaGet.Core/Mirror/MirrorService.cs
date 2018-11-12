using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Services;
using BaGet.Protocol;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace BaGet.Core.Mirror
{
    public class MirrorService : IMirrorService
    {
        private readonly IPackageService _localPackages;
        private readonly IPackageMetadataService _upstreamFeed;
        private readonly IPackageDownloader _downloader;
        private readonly IIndexingService _indexer;
        private readonly ILogger<MirrorService> _logger;

        public MirrorService(
            IPackageService localPackages,
            IPackageMetadataService upstreamFeed,
            IPackageDownloader downloader,
            IIndexingService indexer,
            ILogger<MirrorService> logger)
        {
            _localPackages = localPackages ?? throw new ArgumentNullException(nameof(localPackages));
            _upstreamFeed = upstreamFeed ?? throw new ArgumentNullException(nameof(upstreamFeed));
            _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
            _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task MirrorAsync(string id, CancellationToken cancellationToken)
        {
            if (await _localPackages.ExistsAsync(id))
            {
                return;
            }

            _logger.LogInformation("Package {PackageId} does not exist locally. Mirroring...", id);

            var versions = await _upstreamFeed.GetAllVersionsAsync(id, includeUnlisted: true);

            _logger.LogInformation(
                "Found {VersionsCount} versions for package {PackageId} on upstream feed. Indexing the 10 latest...",
                versions.Count,
                id);

            foreach (var version in versions.OrderByDescending(v => v).Take(10))
            {
                var packageUri = await _upstreamFeed.GetPackageContentUriAsync(id, version);

                await IndexFromSourceAsync(packageUri, cancellationToken);
            }

            _logger.LogInformation("Finished indexing {PackageId} from the upstream feed", id);
        }

        private async Task IndexFromSourceAsync(Uri packageUri, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("Attempting to mirror package {PackageUri}...", packageUri);

            try
            {
                using (var stream = await _downloader.DownloadOrNullAsync(packageUri, cancellationToken))
                {
                    if (stream == null)
                    {
                        _logger.LogWarning(
                            "Failed to download package at {PackageUri}",
                            packageUri);

                        return;
                    }

                    _logger.LogInformation("Downloaded package at {PackageUri}, indexing...", packageUri);

                    var result = await _indexer.IndexAsync(stream, cancellationToken);

                    _logger.LogInformation(
                        "Finished indexing package at {PackageUri} with result {Result}",
                        packageUri,
                        result);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to mirror package at {PackageUri}", packageUri);
            }
        }
    }
}
