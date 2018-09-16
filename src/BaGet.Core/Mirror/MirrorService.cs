using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Services;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace BaGet.Core.Mirror
{
    public class MirrorService : IMirrorService
    {
        private readonly Uri _packageBaseAddress;
        private readonly IPackageService _localPackages;
        private readonly IPackageDownloader _downloader;
        private readonly IIndexingService _indexer;
        private readonly ILogger<MirrorService> _logger;

        public MirrorService(
            Uri packageBaseAddress,
            IPackageService localPackages,
            IPackageDownloader downloader,
            IIndexingService indexer,
            ILogger<MirrorService> logger)
        {
            _packageBaseAddress = packageBaseAddress ?? throw new ArgumentNullException(nameof(packageBaseAddress));
            _localPackages = localPackages ?? throw new ArgumentNullException(nameof(localPackages));
            _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
            _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task MirrorAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        {
            if (await _localPackages.ExistsAsync(id, version))
            {
                return;
            }

            var idString = id.ToLowerInvariant();
            var versionString = version.ToNormalizedString().ToLowerInvariant();

            await IndexFromSourceAsync(idString, versionString, cancellationToken);
        }

        private async Task IndexFromSourceAsync(string id, string version, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("Attempting to mirror package {Id} {Version}...", id, version);

            try
            {
                // See https://github.com/NuGet/NuGet.Client/blob/4eed67e7e159796ae486d2cca406b283e23b6ac8/src/NuGet.Core/NuGet.Protocol/Resources/DownloadResourceV3.cs#L82
                var packageUri = new Uri(_packageBaseAddress, $"{id}/{version}/{id}.{version}.nupkg");

                using (var stream = await _downloader.DownloadOrNullAsync(packageUri, cancellationToken))
                {
                    if (stream == null)
                    {
                        _logger.LogWarning(
                            "Failed to download package {Id} {Version} at {PackageUri}",
                            id,
                            version,
                            packageUri);

                        return;
                    }

                    _logger.LogInformation("Downloaded package {Id} {Version}, indexing...", id, version);

                    var result = await _indexer.IndexAsync(stream);

                    _logger.LogInformation(
                        "Finished indexing package {Id} {Version} with result {Result}",
                        id,
                        version,
                        result);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to mirror package {Id} {Version}", id, version);
            }
        }
    }
}
