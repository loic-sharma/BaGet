using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Azure.Extensions;
using BaGet.Core.Entities;
using BaGet.Core.Services;
using Microsoft.Azure.Cosmos.Table;
using NuGet.Versioning;

namespace BaGet.Azure
{
    /// <summary>
    /// Stores the metadata of packages using Azure Table Storage.
    /// </summary>
    public partial class TablePackageService : IPackageService
    {
        private const string TableName = "BaGet";

        private readonly CloudTable _table;

        public TablePackageService(CloudTableClient client)
        {
            _table = client?.GetTableReference(TableName) ?? throw new ArgumentNullException(nameof(client));
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
            var operation = TableOperation.Retrieve<PackageEntity>(
                id.ToLowerInvariant(),
                version.ToNormalizedString().ToLowerInvariant(),
                new List<string> { nameof(PackageEntity.Downloads) });

            var result = await _table.ExecuteAsync(operation);
            var entity = result.Result as PackageEntity;

            if (entity == null)
            {
                return false;
            }

            entity.Downloads += 1;

            try
            {
                await _table.ExecuteAsync(TableOperation.Merge(entity));
            }
            catch (StorageException e) when (e.IsPreconditionFailedException())
            {
                // TODO
            }

            return true;
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
                    TableQuery.GenerateFilterCondition(nameof(PackageEntity.Listed), QueryComparisons.Equal, "false"));
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
            return await TryUpdatePackageAsync(id, version, TableOperation.Delete);
        }

        public async Task<bool> RelistPackageAsync(string id, NuGetVersion version)
        {
            return await TryUpdatePackageAsync(id, version, package =>
            {
                package.Listed = true;

                return TableOperation.Merge(package);
            });
        }

        public async Task<bool> UnlistPackageAsync(string id, NuGetVersion version)
        {
            return await TryUpdatePackageAsync(id, version, package =>
            {
                package.Listed = false;

                return TableOperation.Merge(package);
            });
        }

        private List<string> MinimalColumnSet => new List<string> { "PartitionKey" };

        private async Task<bool> TryUpdatePackageAsync(
            string id,
            NuGetVersion version,
            Func<PackageEntity, TableOperation> update)
        {
            var operation = update(new PackageEntity
            {
                PartitionKey = id.ToLowerInvariant(),
                RowKey = version.ToNormalizedString().ToLowerInvariant(),
                ETag = "*"
            });

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
