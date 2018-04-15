using System;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Azure.Search;
using BaGet.Core.Entities;
using BaGet.Tools.AzureSearchImporter.Entities;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
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
        private readonly SearchServiceClient _searchClient;
        private readonly ILogger<Initializer> _logger;

        public Initializer(
            IContext bagetContext,
            IndexerContext indexerContext,
            SearchServiceClient searchClient,
            ILogger<Initializer> logger)
        {
            _bagetContext = bagetContext ?? throw new ArgumentNullException(nameof(bagetContext));
            _indexerContext = indexerContext ?? throw new ArgumentNullException(nameof(indexerContext));
            _searchClient = searchClient ?? throw new ArgumentNullException(nameof(searchClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task InitializeAsync()
            => Task.WhenAll(
                InitializeIndex(),
                InitializeStateAsync());

        private async Task InitializeIndex()
        {
            if (await _searchClient.Indexes.ExistsAsync(PackageModel.IndexName))
            {
                _logger.LogInformation("Search index already exists");
                return;
            }

            _logger.LogInformation("Search index does not exist, creating...");

            await _searchClient.Indexes.CreateAsync(new Index
            {
                Name = PackageModel.IndexName,
                Fields = FieldBuilder.BuildForType<PackageModel>()
            });

            _logger.LogInformation("Created search index");
        }

        private async Task InitializeStateAsync()
        {
            if (await _indexerContext.PackageIds.AnyAsync())
            {
                _logger.LogInformation("Indexer state is already initialized");
                return;
            }

            _logger.LogInformation("Unitialized state. Finding packages to track in indexer state...");

            var packageIds = await _bagetContext.Packages
                .Select(p => p.Id)
                .Distinct()
                .ToListAsync();

            _logger.LogInformation("Found {PackageIdCount} package ids to track in indexer state", packageIds.Count);

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

                _logger.LogInformation("Saving package id batch {BatchCount} to indexer state...", batchCount);

                await _indexerContext.SaveChangesAsync();
                batchCount++;
            }

            _logger.LogInformation("Finished adding {PackageIdCount} package ids to indexer state");
        }
    }
}
