using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using BaGet.Core.Services;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using System.Threading;

namespace BaGet.Services.Remote
{
    using NuGetLogger = NuGet.Common.ILogger;

    public class RemotePackageService : IPackageService
    {
        private readonly string _packageBaseAddress;
        private readonly IPackageService _cache;
        private readonly IPackageDownloader _downloader;
        private readonly IIndexingService _indexer;
        private readonly ILogger<RemotePackageService> _logger;
        private readonly NuGetLogger _nugetLogger;

        public RemotePackageService(
            string packageBaseAddress,
            IPackageService cache,
            IPackageDownloader downloader,
            IIndexingService indexer,
            ILogger<RemotePackageService> logger)
        {
            _packageBaseAddress = packageBaseAddress?.Trim('/') ?? throw new ArgumentNullException(nameof(packageBaseAddress));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
            _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _nugetLogger = new Logger(_logger);
        }

        public async Task<bool> ExistsAsync(string id, NuGetVersion version)
        {
            if (await _cache.ExistsAsync(id, version))
            {
                return true;
            }

            return await TryIndexFromSourceAsync(id, version);
        }

        public async Task<IReadOnlyList<Package>> FindAsync(string id)
        {
            var packages = await _cache.FindAsync(id);

            if (packages.Count != 0)
            {
                return packages;
            }

            if (!await TryIndexFromSourceAsync(id))
            {
                return new List<Package>();
            }

            return await _cache.FindAsync(id);
        }

        public async Task<Package> FindAsync(string id, NuGetVersion version)
        {
            var package = await _cache.FindAsync(id, version);

            if (package != null)
            {
                return package;
            }

            if (!await TryIndexFromSourceAsync(id, version))
            {
                return null;
            }

            return await _cache.FindAsync(id, version);
        }

        public async Task<bool> UnlistPackageAsync(string id, NuGetVersion version)
        {
            if (!await ExistsAsync(id, version))
            {
                return false;
            }

            return await _cache.UnlistPackageAsync(id, version);
        }

        public async Task<bool> RelistPackageAsync(string id, NuGetVersion version)
        {
            if (!await ExistsAsync(id, version))
            {
                return false;
            }

            return await _cache.RelistPackageAsync(id, version);
        }

        private Task<bool> TryIndexFromSourceAsync(string id)
        {
            /*
            var packageFinder = await _sourceRepository.GetResourceAsync<FindPackageByIdResource>();

            var versions = await packageFinder.GetAllVersionsAsync(
                id,
                _cacheContext,
                _nugetLogger,
                CancellationToken.None);

            var tasks = versions.Select(v => TryIndexFromSourceAsync(id, v)).ToList();

            return (await Task.WhenAll(tasks)).All(r => r);
            */

            return Task.FromResult(false);
        }

        private async Task<bool> TryIndexFromSourceAsync(string id, NuGetVersion version)
        {
            _logger.LogInformation("Attempting to index package {Id} {Version} from upstream source...",
                id,
                version.ToNormalizedString());

            try
            {
                // See https://github.com/NuGet/NuGet.Client/blob/4eed67e7e159796ae486d2cca406b283e23b6ac8/src/NuGet.Core/NuGet.Protocol/Resources/DownloadResourceV3.cs#L82
                var idPath = id.ToLowerInvariant();
                var versionPath = version.ToNormalizedString().ToLowerInvariant();

                var packageUri = new Uri($"{_packageBaseAddress}/{idPath}/{versionPath}/{idPath}.{versionPath}.nupkg");

                // TODO: DownloadAsync throws when the package doesn't exist. This could be cleaner.
                using (var stream = await _downloader.DownloadAsync(packageUri, CancellationToken.None))
                {
                    var indexingResult = await _indexer.IndexAsync(stream);

                    switch (indexingResult)
                    {
                        case IndexingResult.InvalidPackage:
                            return false;

                        case IndexingResult.Success:
                        case IndexingResult.PackageAlreadyExists:
                            return true;

                        default:
                            _logger.LogError("Unknown indexing result: {IndexingResult}", indexingResult);

                            throw new InvalidOperationException($"Unknown indexing result: {indexingResult}");
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to index package");

                return false;
            }
        }

        private class Logger : NuGetLogger
        {
            private readonly ILogger<RemotePackageService> _logger;

            public Logger(ILogger<RemotePackageService> logger)
            {
                _logger = logger;
            }

            public void LogDebug(string data) => _logger.LogDebug(data);
            public void LogError(string data) => _logger.LogError(data);
            public void LogErrorSummary(string data) => _logger.LogError(data);
            public void LogInformation(string data) => _logger.LogInformation(data);
            public void LogInformationSummary(string data) => _logger.LogInformation(data);
            public void LogMinimal(string data) => _logger.LogTrace(data);
            public void LogVerbose(string data) => _logger.LogTrace(data);
            public void LogWarning(string data) => _logger.LogWarning(data);
        }
    }
}
