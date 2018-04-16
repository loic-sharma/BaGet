using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Azure.Search;
using BaGet.Tools.AzureSearchImporter.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoreLinq;

namespace BaGet.Tools.AzureSearchImporter
{
    public class Importer
    {
        private const int ImportBatchSize = 200;

        private readonly IndexerContext _context;
        private readonly BatchIndexer _indexer;
        private readonly ILogger<Importer> _logger;

        public Importer(IndexerContext context, BatchIndexer indexer, ILogger<Importer> logger)
        {
            _context = context ?? throw new ArgumentException(nameof(context));
            _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task ImportAsync(int skip = 0)
        {
            _logger.LogInformation("Starting import with skip {Skip}...", skip);

            var batchCount = 1;
            var left = await _context.PackageIds
                .Where(p => !p.Done)
                .CountAsync();

            _logger.LogInformation("{PackageIdsLeft} package ids left to import", left);

            List<PackageId> batch;

            do
            {
                _logger.LogInformation("Importing batch {BatchCount}...", batchCount);

                batch = await _context.PackageIds
                    .Where(p => !p.Done)
                    .OrderBy(p => p.Key)
                    .Skip(skip)
                    .Take(ImportBatchSize)
                    .ToListAsync();

                await _indexer.IndexAsync(batch.Select(p => p.Value));

                foreach (var package in batch)
                {
                    package.Done = true;
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation("Imported batch {BatchCount}", batchCount);
                batchCount++;
            }
            while (batch.Count > 0);

            _logger.LogInformation("Finished importing");
        }
    }
}
