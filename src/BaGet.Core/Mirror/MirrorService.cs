using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Services;
using Microsoft.Extensions.Logging;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using NuGet.Protocol;
using NuGet.Common;
using System.Linq;

namespace BaGet.Core.Mirror
{
    public class MirrorService : IMirrorService
    {
        private readonly Uri _packageBaseAddress;
        private readonly IPackageService _localPackages;
        private readonly IPackageDownloader _downloader;
        private readonly IIndexingService _indexer;
        private readonly ILogger<MirrorService> _logger;
        private readonly SourceRepository _sourceRepository;
        private SourceCacheContext _cacheContext;
        NuGetLoggerAdapter<MirrorService> _loggerAdapter;
        private PackageMetadataResourceV3 _metadataSearch;
        private RemoteV3FindPackageByIdResource _versionSearch;

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
            this._loggerAdapter = new NuGetLoggerAdapter<MirrorService>(_logger);
            List<Lazy<INuGetResourceProvider>> providers = new List<Lazy<INuGetResourceProvider>>();
            providers.AddRange(Repository.Provider.GetCoreV3());
            providers.Add(new Lazy<INuGetResourceProvider>(() => new PackageMetadataResourceV3Provider()));
            PackageSource packageSource = new PackageSource("https://api.nuget.org/v3/index.json"); //TODO needs options
            _sourceRepository = new SourceRepository(packageSource, providers);
            _cacheContext = new SourceCacheContext();
            var httpSource = _sourceRepository.GetResource<HttpSourceResource>();
            RegistrationResourceV3 regResource = _sourceRepository.GetResource<RegistrationResourceV3>();
            ReportAbuseResourceV3 reportAbuseResource = _sourceRepository.GetResource<ReportAbuseResourceV3>();
            _metadataSearch = new PackageMetadataResourceV3(httpSource.HttpSource, regResource, reportAbuseResource);
            _versionSearch = new RemoteV3FindPackageByIdResource(_sourceRepository, httpSource.HttpSource);
        }

        public async Task<IEnumerable<IPackageSearchMetadata>> FindUpstreamMetadataAsync(string id, CancellationToken ct) {
            return await _metadataSearch.GetMetadataAsync(id, true, false, _cacheContext, _loggerAdapter, ct);
        }

        public async Task<IReadOnlyList<string>> FindUpstreamAsync(string id, CancellationToken ct)
        {           
            var versions = await _versionSearch.GetAllVersionsAsync(id, _cacheContext, _loggerAdapter, ct);
            return versions.Select(v => v.ToNormalizedString()).ToList();
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
