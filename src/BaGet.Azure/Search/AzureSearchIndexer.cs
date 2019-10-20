using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.Logging;

namespace BaGet.Azure.Search
{
    public class AzureSearchIndexer : ISearchIndexer
    {
        private readonly IPackageService _packages;
        private readonly IndexActionBuilder _actionBuilder;
        private readonly AzureSearchBatchIndexer _batchIndexer;
        private readonly ILogger<AzureSearchIndexer> _logger;

        public AzureSearchIndexer(
            IPackageService packages,
            IndexActionBuilder actionBuilder,
            AzureSearchBatchIndexer batchIndexer,
            ILogger<AzureSearchIndexer> logger)
        {
            _packages = packages ?? throw new ArgumentNullException(nameof(packages));
            _actionBuilder = actionBuilder ?? throw new ArgumentNullException(nameof(actionBuilder));
            _batchIndexer = batchIndexer ?? throw new ArgumentNullException(nameof(batchIndexer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task IndexAsync(Package package, CancellationToken cancellationToken = default)
        {
            var packages = await _packages.FindAsync(package.Id, includeUnlisted: false);

            if (packages.Count == 0)
            {
                _logger.LogError("Could not find package with id {PackageId}", package.Id);

                throw new ArgumentException($"Package '{package.Id}' does not exist", nameof(package));
            }

            var actions = _actionBuilder.UpdatePackage(
                new PackageRegistration(
                package.Id,
                packages));

            foreach (var action in actions)
            {
                await _batchIndexer.EnqueueIndexActionAsync(action, cancellationToken);
            }

            await _batchIndexer.PushBatchesAsync(cancellationToken);
        }
    }
}
