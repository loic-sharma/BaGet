using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Azure.Extensions;
using BaGet.Core.Entities;
using BaGet.Core.Services;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace BaGet.Azure
{
    /// <summary>
    /// Stores the metadata of packages using Azure Table Storage.
    /// </summary>
    public partial class TablePackageService : IPackageService
    {
        // TODO: Make the table name a config
        private const string TableName = "BaGet";
        private const int MaxPreconditionFailures = 5;

        private readonly CloudTable _table;
        private readonly ILogger<TablePackageService> _logger;

        public TablePackageService(CloudTableClient client, ILogger<TablePackageService> logger)
        {
            _table = client?.GetTableReference(TableName) ?? throw new ArgumentNullException(nameof(client));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<PackageAddResult> AddAsync(Package package)
        {
            var entity = PackageEntity.FromPackage(package);
            var operation = TableOperation.Insert(entity);

            try
            {
                await _table.ExecuteAsync(operation);
            }
            catch (StorageException e) when (e.IsAlreadyExistsException())
            {
                return PackageAddResult.PackageAlreadyExists;
            }

            return PackageAddResult.Success;
        }

        public async Task<bool> AddDownloadAsync(string id, NuGetVersion version)
        {
            return await RetryOnPreconditionFailures(async () =>
            {
                var operation = TableOperation.Retrieve<PackageDownloadsEntity>(
                    id.ToLowerInvariant(),
                    version.ToNormalizedString().ToLowerInvariant());

                var result = await _table.ExecuteAsync(operation);
                var entity = result.Result as PackageDownloadsEntity;

                if (entity == null)
                {
                    return false;
                }

                entity.Downloads += 1;

                await _table.ExecuteAsync(TableOperation.Merge(entity));
                return true;
            });
        }

        public async Task<bool> ExistsAsync(string id, NuGetVersion version = null)
        {
            if (version == null)
            {
                return await ExistsAsync(id);
            }

            var operation = TableOperation.Retrieve<PackageEntity>(
                            id.ToLowerInvariant(),
                            version.ToNormalizedString().ToLowerInvariant(),
                            MinimalColumnSet);

            var result = await _table.ExecuteAsync(operation);
            var entity = result.Result as PackageEntity;

            return entity != null;
        }

        private async Task<bool> ExistsAsync(string id)
        {
            var filter = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, id.ToLowerInvariant());
            var query = new TableQuery<PackageEntity>()
                .Select(MinimalColumnSet)
                .Where(filter)
                .Take(1);

            var result = await _table.ExecuteQuerySegmentedAsync(query, token: null);

            return result.Results.Any();
        }

        public async Task<IReadOnlyList<Package>> FindAsync(string id, bool includeUnlisted)
        {
            var filter = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, id.ToLowerInvariant());
            if (!includeUnlisted)
            {
                filter = TableQuery.CombineFilters(
                    filter,
                    TableOperators.And,
                    TableQuery.GenerateFilterConditionForBool(nameof(PackageEntity.Listed), QueryComparisons.Equal, true));
            }

            var query = new TableQuery<PackageEntity>().Where(filter);
            var results = new List<Package>();

            // Request 500 results at a time from the server.
            TableContinuationToken token = null;
            query.TakeCount = 500;

            do
            {
                var segment = await _table.ExecuteQuerySegmentedAsync(query, token);

                token = segment.ContinuationToken;

                // Write out the properties for each entity returned.
                results.AddRange(segment.Results.Select(r => r.AsPackage()));
            }
            while (token != null);

            return results;
        }

        public async Task<Package> FindOrNullAsync(string id, NuGetVersion version, bool includeUnlisted)
        {
            var operation = TableOperation.Retrieve<PackageEntity>(
                id.ToLowerInvariant(),
                version.ToNormalizedString().ToLowerInvariant());

            var result = await _table.ExecuteAsync(operation);
            var entity = result.Result as PackageEntity;

            if (entity == null)
            {
                return null;
            }

            // Filter out the package if it's unlisted.
            if (!includeUnlisted && !entity.Listed)
            {
                return null;
            }

            return entity.AsPackage();
        }

        public async Task<bool> HardDeletePackageAsync(string id, NuGetVersion version)
        {
            return await TryUpdatePackageAsync(
                id,
                version,
                new PackageEntity(),
                TableOperation.Delete);
        }

        public async Task<bool> RelistPackageAsync(string id, NuGetVersion version)
        {
            return await TryUpdatePackageAsync(
                id,
                version,
                new PackageListingEntity { Listed = true },
                TableOperation.Merge);
        }

        public async Task<bool> UnlistPackageAsync(string id, NuGetVersion version)
        {
            return await TryUpdatePackageAsync(
                id,
                version,
                new PackageListingEntity { Listed = false },
                TableOperation.Merge);
        }

        private List<string> MinimalColumnSet => new List<string> { "PartitionKey" };

        private async Task<TResult> RetryOnPreconditionFailures<TResult>(Func<Task<TResult>> action)
        {
            var attempt = 0;

            while (true)
            {
                try
                {
                    return await action();
                }
                catch (StorageException e)
                    when (attempt < MaxPreconditionFailures && e.IsPreconditionFailedException())
                {
                    _logger.LogWarning(
                        e,
                        $"Retrying due to precondition failure, attempt {{Attempt}} of {MaxPreconditionFailures}..",
                        attempt);
                }
            }
        }

        private async Task<bool> TryUpdatePackageAsync(
            string id,
            NuGetVersion version,
            TableEntity entity,
            Func<TableEntity, TableOperation> prepare)
        {
            entity.PartitionKey = id.ToLowerInvariant();
            entity.RowKey = version.ToNormalizedString().ToLowerInvariant();
            entity.ETag = "*";

            var operation = prepare(entity);

            try
            {
                await _table.ExecuteAsync(operation);
            }
            catch (StorageException e) when (e.IsNotFoundException())
            {
                return false;
            }

            return true;
        }
    }
}
