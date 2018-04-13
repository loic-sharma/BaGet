using System;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using BaGet.Tools.AzureSearchImporter.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoreLinq;

namespace BaGet.Tools.AzureSearchImporter
{
    public class Initializer
    {
        public const int InitializationBatchSize = 100;

        private readonly IContext _bagetContext;
        private readonly IndexerContext _indexerContext;
        private readonly ILogger<Initializer> _logger;

        public Initializer(IContext bagetContext, IndexerContext indexerContext, ILogger<Initializer> logger)
        {
            _bagetContext = bagetContext ?? throw new ArgumentNullException(nameof(bagetContext));
            _indexerContext = indexerContext ?? throw new ArgumentNullException(nameof(indexerContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InitializeAsync(bool force = false)
        {
            if (!force && await _indexerContext.PackageIds.AnyAsync())
            {
                _logger.LogInformation("Skipping initialization");
                return;
            }

            _logger.LogInformation("Finding packages to initialize...");

            var packageIds = await _bagetContext.Packages
                .GroupBy(p => p.Id)
                .OrderByDescending(g => g.Sum(p => p.Downloads))
                .Select(g => g.Key)
                .ToListAsync();

            _logger.LogInformation("Found {PackageIdCount} package ids to initialize", packageIds.Count);

            var batchCount = 1;

            foreach (var batch in packageIds.Batch(InitializationBatchSize))
            {
                foreach (var packageId in batch)
                {
                    _indexerContext.PackageIds.Add(new PackageId
                    {
                        Value = packageId,
                        Done = false,
                    });
                }

                _logger.LogInformation("Saving batch {BatchCount}", batchCount);

                await _indexerContext.SaveChangesAsync();
                batchCount++;
            }
        }
    }
}
