using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace BaGet.Azure;

/// <summary>
/// Stores the metadata of packages using Azure Table Storage.
/// </summary>
public class TablePackageDatabase : IPackageDatabase
{
    private const string TableName = "Packages";
    private const int MaxPreconditionFailures = 5;

    private readonly TableOperationBuilder _operationBuilder;
    private readonly CloudTable _table;
    private readonly ILogger<TablePackageDatabase> _logger;
    private readonly IRepositoryContext? _repository;

    public TablePackageDatabase(
        TableOperationBuilder operationBuilder,
        CloudTableClient? client,
        ILogger<TablePackageDatabase> logger,
        IRepositoryContext? repository = null)
    {
        _repository = repository;
        _operationBuilder = operationBuilder ?? throw new ArgumentNullException(nameof(operationBuilder));
        _table = client?.GetTableReference(TableName) ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // The table has no repository column: it holds the default repository's packages only. Repositories beyond
    // the default need a relational database (see RepositoryService), so reaching here for another one is a bug in
    // the caller; refuse rather than serve the default repository's packages under another name.
    private void RequireDefaultRepository()
    {
        if (_repository != null && !_repository.Current.IsDefault)
        {
            throw new NotSupportedException(
                $"Azure Table storage supports only the '{Repository.DefaultName}' repository, not '{_repository.Current.Name}'; use a relational database for more repositories.");
        }
    }

    public async Task<PackageAddResult> AddAsync(Package package, CancellationToken cancellationToken)
    {
        RequireDefaultRepository();
        try
        {
            var operation = _operationBuilder.AddPackage(package);

            await _table.ExecuteAsync(operation, cancellationToken);
        }
        catch (StorageException e) when (e.IsAlreadyExistsException())
        {
            return PackageAddResult.PackageAlreadyExists;
        }

        return PackageAddResult.Success;
    }

    public async Task AddDownloadAsync(
        string id,
        NuGetVersion version,
        CancellationToken cancellationToken)
    {
        RequireDefaultRepository();
        var attempt = 0;

        while (true)
        {
            try
            {
                var operation = TableOperation.Retrieve<PackageDownloadsEntity>(
                    id.ToLowerInvariant(),
                    version.ToNormalizedString().ToLowerInvariant());

                var result = await _table.ExecuteAsync(operation, cancellationToken);
                var entity = result.Result as PackageDownloadsEntity;

                if (entity == null)
                {
                    return;
                }

                entity.Downloads += 1;

                await _table.ExecuteAsync(TableOperation.Merge(entity), cancellationToken);
                return;
            }
            catch (StorageException e)
                when (attempt < MaxPreconditionFailures && e.IsPreconditionFailedException())
            {
                attempt++;
                _logger.LogWarning(
                    e,
                    $"Retrying due to precondition failure, attempt {{Attempt}} of {MaxPreconditionFailures}..",
                    attempt);
            }
        }
    }

    public async Task<bool> ExistsAsync(string? id, CancellationToken cancellationToken)
    {
        RequireDefaultRepository();
        #pragma warning disable CS8602
        var filter = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, id.ToLowerInvariant());
        #pragma warning restore CS8602
        var query = new TableQuery<PackageEntity>()
            .Select(MinimalColumnSet)
            .Where(filter)
            .Take(1);

        var result = await _table.ExecuteQuerySegmentedAsync(query, token: null, cancellationToken);

        return result.Results.Any();
    }

    public async Task<bool> ExistsAsync(
        string? id,
        NuGetVersion version,
        CancellationToken cancellationToken)
    {
        RequireDefaultRepository();
        #pragma warning disable CS8602
        var operation = TableOperation.Retrieve<PackageEntity>(
            id.ToLowerInvariant(),
            version.ToNormalizedString().ToLowerInvariant(),
            MinimalColumnSet);
        #pragma warning restore CS8602

        var execution = await _table.ExecuteAsync(operation, cancellationToken);

        return execution.Result is PackageEntity;
    }

    public async Task<IReadOnlyList<Package>> FindAsync(string? id, bool includeUnlisted, CancellationToken cancellationToken)
    {
        RequireDefaultRepository();
        #pragma warning disable CS8602
        var filter = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, id.ToLowerInvariant());
        #pragma warning restore CS8602
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
        TableContinuationToken? token = null;
        query.TakeCount = 500;

        do
        {
            var segment = await _table.ExecuteQuerySegmentedAsync(query, token, cancellationToken);

            token = segment.ContinuationToken;

            // Write out the properties for each entity returned.
            results.AddRange(segment.Results.Select(r => r.AsPackage()));
        }
        while (token != null);

        return results.OrderBy(p => p.Version).ToList();
    }

    public async Task<Package?> FindOrNullAsync(
        string? id,
        NuGetVersion version,
        bool includeUnlisted,
        CancellationToken cancellationToken)
    {
        RequireDefaultRepository();
        #pragma warning disable CS8602
        var operation = TableOperation.Retrieve<PackageEntity>(
            id.ToLowerInvariant(),
            version.ToNormalizedString().ToLowerInvariant());
        #pragma warning restore CS8602

        var result = await _table.ExecuteAsync(operation, cancellationToken);
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

    public async Task<bool> HardDeletePackageAsync(string? id, NuGetVersion version, CancellationToken cancellationToken)
    {
        RequireDefaultRepository();
        return await TryUpdatePackageAsync(
            _operationBuilder.HardDeletePackage(id, version),
            cancellationToken);
    }

    public async Task<bool> RelistPackageAsync(string? id, NuGetVersion version, CancellationToken cancellationToken)
    {
        RequireDefaultRepository();
        return await TryUpdatePackageAsync(
            _operationBuilder.RelistPackage(id, version),
            cancellationToken);
    }

    public async Task<bool> UnlistPackageAsync(string? id, NuGetVersion version, CancellationToken cancellationToken)
    {
        RequireDefaultRepository();
        return await TryUpdatePackageAsync(
            _operationBuilder.UnlistPackage(id, version),
            cancellationToken);
    }

    private List<string> MinimalColumnSet => new() { "PartitionKey" };

    private async Task<bool> TryUpdatePackageAsync(TableOperation operation, CancellationToken cancellationToken)
    {
        try
        {
            await _table.ExecuteAsync(operation, cancellationToken);
        }
        catch (StorageException e) when (e.IsNotFoundException())
        {
            return false;
        }

        return true;
    }

    // Deprecation and vulnerability metadata are stored in relational tables. The Azure
    // Table backend has no schema for them, and one cannot be verified without an Azure
    // account, so it says so rather than silently discarding what it is given.
    private const string MetadataNotSupported =
        "Package deprecation and vulnerability metadata are not supported when the database is Azure Table storage.";

    public Task<bool> SetDeprecationAsync(
        string? id,
        NuGetVersion version,
        PackageDeprecation deprecation,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException(MetadataNotSupported);
    }

    public Task<bool> RemoveDeprecationAsync(string? id, NuGetVersion version, CancellationToken cancellationToken)
    {
        throw new NotSupportedException(MetadataNotSupported);
    }

    public Task<bool> SetVulnerabilitiesAsync(
        string id,
        NuGetVersion version,
        IReadOnlyList<PackageVulnerability> vulnerabilities,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException(MetadataNotSupported);
    }

    public Task<IReadOnlyList<PackageVulnerability>> GetAllVulnerabilitiesAsync(CancellationToken cancellationToken)
    {
        // Nothing can have been stored, so there is nothing to report.
        return Task.FromResult<IReadOnlyList<PackageVulnerability>>(Array.Empty<PackageVulnerability>());
    }
}