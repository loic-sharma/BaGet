using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Protocol.Models;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace BaGet.Azure
{
    public class TableSearchService : ISearchService
    {
        private const string TableName = "Packages";

        private static readonly IReadOnlyList<string> EmptyStringList = new List<string>();

        private static readonly Task<DependentsResponse> EmptyDependentsResponseTask =
            Task.FromResult(new DependentsResponse
            {
                TotalHits = 0,
                Data = EmptyStringList
            });

        private readonly CloudTable _table;
        private readonly IUrlGenerator _url;

        public TableSearchService(
            CloudTableClient client,
            IUrlGenerator url)
        {
            _table = client?.GetTableReference(TableName) ?? throw new ArgumentNullException(nameof(client));
            _url = url ?? throw new ArgumentNullException(nameof(url));
        }

        public async Task<AutocompleteResponse> AutocompleteAsync(
            string query = null,
            AutocompleteType type = AutocompleteType.PackageIds,
            int skip = 0,
            int take = 20,
            bool includePrerelease = true,
            bool includeSemVer2 = true,
            CancellationToken cancellationToken = default)
        {
            // TODO: Support version autocomplete
            var results = await SearchInternalAsync(
                query,
                skip,
                take,
                includePrerelease,
                includeSemVer2,
                cancellationToken);

            return new AutocompleteResponse
            {
                TotalHits = results.Count,
                Data = results.Select(ToAutocompleteResult).ToList(),
            };
        }

        public async Task<SearchResponse> SearchAsync(
            string query = null,
            int skip = 0,
            int take = 20,
            bool includePrerelease = true,
            bool includeSemVer2 = true,
            string packageType = null,
            string framework = null,
            CancellationToken cancellationToken = default)
        {
            var results = await SearchInternalAsync(
                query,
                skip,
                take,
                includePrerelease,
                includeSemVer2,
                cancellationToken);

            return new SearchResponse
            {
                TotalHits = results.Count,
                Data = results.Select(ToSearchResult).ToList()
            };
        }

        public Task<DependentsResponse> FindDependentsAsync(string packageId, int skip = 0, int take = 20, CancellationToken cancellationToken = default)
        {
            return EmptyDependentsResponseTask;
        }

        public Task IndexAsync(Package package, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        private async Task<List<List<TablePackageService.PackageEntity>>> SearchInternalAsync(
            string searchText,
            int skip,
            int take,
            bool includePrerelease,
            bool includeSemVer2,
            CancellationToken cancellationToken)
        {
            var query = new TableQuery<TablePackageService.PackageEntity>();
            query = query.Where(GenerateSearchFilter(searchText, includePrerelease, includeSemVer2));
            query.TakeCount = 500;

            string lastPartitionKey = null;
            var results = new List<List<TablePackageService.PackageEntity>>();

            TableContinuationToken token = null;
            do
            {
                var segment = await _table.ExecuteQuerySegmentedAsync(query, token, cancellationToken);

                token = segment.ContinuationToken;

                foreach (var result in segment.Results)
                {
                    if (lastPartitionKey != result.PartitionKey)
                    {
                        results.Add(new List<TablePackageService.PackageEntity>());
                        lastPartitionKey = result.PartitionKey;
                    }

                    results.Last().Add(result);
                }
            }
            while (token != null && results.Count < take + skip);

            return results.Skip(skip).Take(take).ToList();
        }

        private string GenerateSearchFilter(string searchText, bool includePrerelease, bool includeSemVer2)
        {
            var result = "";

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                // Filter to rows where the "searchText" prefix matches on the partition key.
                var prefix = searchText?.TrimEnd().Split(separator: null).Last() ?? string.Empty;

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

                result = GenerateAnd(partitionLowerFilter, partitionUpperFilter);
            }

            // Filter to rows that are listed.
            result = GenerateAnd(
                result,
                GenerateIsTrue(nameof(TablePackageService.PackageEntity.Listed)));

            if (!includePrerelease)
            {
                result = GenerateAnd(
                    result,
                    GenerateIsFalse(nameof(TablePackageService.PackageEntity.IsPrerelease)));
            }

            if (!includeSemVer2)
            {
                result = GenerateAnd(
                    result,
                    TableQuery.GenerateFilterCondition(
                        nameof(TablePackageService.PackageEntity.SemVerLevel),
                        QueryComparisons.Equal,
                        "0"));
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

        private string ToAutocompleteResult(IReadOnlyList<TablePackageService.PackageEntity> packages)
        {
            // TODO: This should find the latest version and return its package Id.
            return packages.Last().Id;
        }

        private SearchResult ToSearchResult(IReadOnlyList<TablePackageService.PackageEntity> packages)
        {
            NuGetVersion latestVersion = null;
            TablePackageService.PackageEntity latest = null;
            var versions = new List<SearchResultVersion>();
            long totalDownloads = 0;

            foreach (var package in packages)
            {
                var version = NuGetVersion.Parse(package.OriginalVersion);

                totalDownloads += package.Downloads;
                versions.Add(new SearchResultVersion
                {
                    RegistrationLeafUrl = _url.GetRegistrationLeafUrl(package.Id, version),
                    Version = package.NormalizedVersion,
                    Downloads = package.Downloads,
                });

                if (latestVersion == null || version > latestVersion)
                {
                    latest = package;
                }
            }

            return new SearchResult
            {
                PackageId = latest.Id,
                Version = latest.NormalizedVersion,
                Description = latest.Description,
                Authors = JsonConvert.DeserializeObject<string[]>(latest.Authors),
                IconUrl = latest.IconUrl,
                LicenseUrl = latest.LicenseUrl,
                ProjectUrl = latest.ProjectUrl,
                RegistrationIndexUrl = _url.GetRegistrationIndexUrl(latest.Id),
                Summary = latest.Summary,
                Tags = JsonConvert.DeserializeObject<string[]>(latest.Tags),
                Title = latest.Title,
                TotalDownloads = totalDownloads,
                Versions = versions,
            };
        }
    }
}
