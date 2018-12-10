using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BaGet.Core.Services
{
    public class PackagesReconciler
    {
        private readonly IStorageService _storage;
        private readonly IPackageIndexingService _index;
        private readonly ILogger<PackageIndexingService> _logger;

        public PackagesReconciler(
            IStorageService storage,
            IPackageIndexingService index,
            ILogger<PackageIndexingService> logger)
        {
            _storage = storage;
            _index = index;
            _logger = logger;
        }

        public async Task Reconcile(CancellationToken cancellationToken = default)
        {
            var allStored = await _storage.GetPackagePathsAsync(cancellationToken);
            foreach (var stored in allStored)
            {
                try
                {
                    var stream = await _storage.GetAsync(stored, cancellationToken);
                    await _index.IndexAsync(stream, false, true, true, cancellationToken);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to reindex package");
                }
            }
        }
    }
}
