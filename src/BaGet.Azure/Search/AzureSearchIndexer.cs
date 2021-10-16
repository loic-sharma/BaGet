using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.Logging;

namespace BaGet.Azure
{
    public class AzureSearchIndexer : ISearchIndexer
    {
        private readonly IPackageDatabase _packages;
        private readonly IndexActionBuilder _actionBuilder;
        private readonly AzureSearchBatchIndexer _batchIndexer;
        private readonly ILogger<AzureSearchIndexer> _logger;

        public AzureSearchIndexer(
            IPackageDatabase packages,
            IndexActionBuilder actionBuilder,
            AzureSearchBatchIndexer batchIndexer,
            ILogger<AzureSearchIndexer> logger)
        {
            _packages = packages ?? throw new ArgumentNullException(nameof(packages));
            _actionBuilder = actionBuilder ?? throw new ArgumentNullException(nameof(actionBuilder));
            _batchIndexer = batchIndexer ?? throw new ArgumentNullException(nameof(batchIndexer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task IndexAsync(Package package, CancellationToken cancellationToken)
        {
            var packages = await _packages.FindAsync(package.Id, includeUnlisted: false, cancellationToken);

            var actions = _actionBuilder.UpdatePackage(
                new PackageRegistration(
                    package.Id,
                    packages));

            await _batchIndexer.IndexAsync(actions, cancellationToken);
        }
    }
}
