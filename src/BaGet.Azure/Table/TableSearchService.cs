using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Protocol.Models;
using Microsoft.Azure.Cosmos.Table;

namespace BaGet.Azure;

/// <remarks>
/// <see cref="SearchRequest.SortBy"/> is ignored: Azure Table storage cannot order by anything
/// but its keys, and sorting one page in memory would be wrong.
/// </remarks>
public class TableSearchService : ISearchService
{
    private const string TableName = "Packages";

    private readonly CloudTable _table;
    private readonly ISearchResponseBuilder _responseBuilder;

    public TableSearchService(
        CloudTableClient? client,
        ISearchResponseBuilder responseBuilder)
    {
        _table = client?.GetTableReference(TableName) ?? throw new ArgumentNullException(nameof(client));
        _responseBuilder = responseBuilder ?? throw new ArgumentNullException(nameof(responseBuilder));
    }

    public async Task<SearchResponse> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken)
    {
        var results = await SearchAsync(
            request.Query,
            request.Skip,
            request.Take,
            request.IncludePrerelease,
            request.IncludeSemVer2,
            cancellationToken);

        return _responseBuilder.BuildSearch(results.Page, results.TotalHits);
    }

    public async Task<AutocompleteResponse> AutocompleteAsync(
        AutocompleteRequest request,
        CancellationToken cancellationToken)
    {
        var results = await SearchAsync(
            request.Query,
            request.Skip,
            request.Take,
            request.IncludePrerelease,
            request.IncludeSemVer2,
            cancellationToken);

        var packageIds = results.Page.Select(p => p.PackageId).ToList();

        return _responseBuilder.BuildAutocomplete(packageIds, results.TotalHits);
    }

    public async Task<AutocompleteResponse> ListPackageVersionsAsync(
        VersionsRequest request,
        CancellationToken cancellationToken)
    {
        // A package ID is one partition, so its versions are a single partition query.
        var query = new TableQuery<PackageEntity>();
        query = query.Where(GenerateVersionsFilter(request.PackageId, request.IncludePrerelease, request.IncludeSemVer2));

        var packages = await LoadPackagesAsync(query, maxPartitions: 1, cancellationToken);
        var versions = packages
            .OrderBy(p => p.Version)
            .Select(p => p.Version.ToNormalizedString())
            .ToList();

        return _responseBuilder.BuildAutocomplete(versions, versions.Count);
    }

    public Task<DependentsResponse> FindDependentsAsync(string? packageId, CancellationToken cancellationToken)
    {
        var response = _responseBuilder.BuildDependents(new List<PackageDependent>());

        return Task.FromResult(response);
    }

    /// <summary>
    /// A page of package registrations and the number of registrations that matched,
    /// disregarding skip and take.
    /// </summary>
    private readonly struct SearchPage
    {
        public SearchPage(List<PackageRegistration> page, long totalHits)
        {
            Page = page;
            TotalHits = totalHits;
        }

        public List<PackageRegistration> Page { get; }

        public long TotalHits { get; }
    }

    private async Task<SearchPage> SearchAsync(
        string? searchText,
        int skip,
        int take,
        bool includePrerelease,
        bool includeSemVer2,
        CancellationToken cancellationToken)
    {
        var filter = GenerateSearchFilter(searchText, includePrerelease, includeSemVer2);

        var query = new TableQuery<PackageEntity>();
        query = query.Where(filter);
        query.TakeCount = 500;

        var results = await LoadPackagesAsync(query, maxPartitions: skip + take, cancellationToken);

        var registrations = results
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => new PackageRegistration(group.Key, group.ToList()))
            .ToList();

        // Table storage cannot count. The scan above stops after "skip + take" partitions,
        // so when it reached that limit there may be more: count them with a scan that
        // reads only the partition keys of the matching rows.
        var totalHits = registrations.Count < skip + take
            ? registrations.Count
            : await CountPartitionsAsync(filter, cancellationToken);

        var page = registrations
            .Skip(skip)
            .Take(take)
            .ToList();

        return new SearchPage(page, totalHits);
    }

    private async Task<long> CountPartitionsAsync(string filter, CancellationToken cancellationToken)
    {
        var query = new TableQuery<DynamicTableEntity>()
            .Where(filter)
            .Select(new[] { "PartitionKey" });
        query.TakeCount = 1000;

        var partitionKeys = new List<string>();
        TableContinuationToken? token = null;
        do
        {
            var segment = await _table.ExecuteQuerySegmentedAsync(query, token, cancellationToken);

            token = segment.ContinuationToken;
            partitionKeys.AddRange(segment.Results.Select(r => r.PartitionKey));
        }
        while (token != null);

        return CountPartitions(partitionKeys);
    }

    /// <summary>
    /// The number of package IDs among rows in partition key order: each ID is one
    /// partition, and a partition's rows are contiguous.
    /// </summary>
    internal static long CountPartitions(IEnumerable<string> partitionKeys)
    {
        long count = 0;
        string? last = null;

        foreach (var key in partitionKeys)
        {
            if (!string.Equals(key, last, StringComparison.Ordinal))
            {
                count++;
                last = key;
            }
        }

        return count;
    }

    private async Task<IReadOnlyList<Package>> LoadPackagesAsync(
        TableQuery<PackageEntity> query,
        int maxPartitions,
        CancellationToken cancellationToken)
    {
        var results = new List<Package>();

        var partitions = 0;
        string? lastPartitionKey = null;
        TableContinuationToken? token = null;
        do
        {
            var segment = await _table.ExecuteQuerySegmentedAsync(query, token, cancellationToken);

            token = segment.ContinuationToken;

            foreach (var result in segment.Results)
            {
                if (lastPartitionKey != result.PartitionKey)
                {
                    lastPartitionKey = result.PartitionKey;
                    partitions++;

                    // Enough partitions: stop rather than fetch segments only to skip them.
                    if (partitions > maxPartitions)
                    {
                        return results;
                    }
                }

                results.Add(result.AsPackage());
            }
        }
        while (token != null);

        return results;
    }

    /// <summary>
    /// The rows of one package ID that these filters allow.
    /// </summary>
    internal static string GenerateVersionsFilter(string? packageId, bool includePrerelease, bool includeSemVer2)
    {
        if (packageId == null) throw new ArgumentNullException(nameof(packageId));

        var partition = TableQuery.GenerateFilterCondition(
            "PartitionKey",
            QueryComparisons.Equal,
            packageId.ToLowerInvariant());

        return GenerateFilters(partition, includePrerelease, includeSemVer2);
    }

    internal static string GenerateSearchFilter(string? searchText, bool includePrerelease, bool includeSemVer2)
    {
        var result = "";

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            // Filter to rows where the "searchText" prefix matches on the partition key.
            // Partition keys are lowercased IDs, and the comparison is ordinal, so the
            // prefix must be lowercased too or a capitalised query matches nothing.
            var prefix = searchText.TrimEnd().Split(separator: null).Last().ToLowerInvariant();

            var prefixLower = prefix;
            var prefixUpper = prefix + "~";

            var partitionLowerFilter = TableQuery.GenerateFilterCondition(
                "PartitionKey",
                QueryComparisons.GreaterThanOrEqual,
                prefixLower);

            var partitionUpperFilter = TableQuery.GenerateFilterCondition(
                "PartitionKey",
                QueryComparisons.LessThanOrEqual,
                prefixUpper);

            result = TableQuery.CombineFilters(partitionLowerFilter, TableOperators.And, partitionUpperFilter);
        }

        return GenerateFilters(result, includePrerelease, includeSemVer2);
    }

    private static string GenerateFilters(string result, bool includePrerelease, bool includeSemVer2)
    {
        // Filter to rows that are listed.
        result = GenerateAnd(
            result,
            GenerateIsTrue(nameof(PackageEntity.Listed)));

        if (!includePrerelease)
        {
            result = GenerateAnd(
                result,
                GenerateIsFalse(nameof(PackageEntity.IsPrerelease)));
        }

        if (!includeSemVer2)
        {
            result = GenerateAnd(
                result,
                TableQuery.GenerateFilterConditionForInt(
                    nameof(PackageEntity.SemVerLevel),
                    QueryComparisons.Equal,
                    0));
        }

        return result;

        string GenerateAnd(string left, string right)
        {
            if (string.IsNullOrEmpty(left)) return right;

            return TableQuery.CombineFilters(left, TableOperators.And, right);
        }

        string GenerateIsTrue(string propertyName)
        {
            return TableQuery.GenerateFilterConditionForBool(
                propertyName,
                QueryComparisons.Equal,
                givenValue: true);
        }

        string GenerateIsFalse(string propertyName)
        {
            return TableQuery.GenerateFilterConditionForBool(
                propertyName,
                QueryComparisons.Equal,
                givenValue: false);
        }
    }
}