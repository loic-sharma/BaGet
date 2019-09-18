using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Protocol.Models;
using Microsoft.Azure.Search;
using NuGet.Versioning;

namespace BaGet.Azure.Search
{
    using QueryType = Microsoft.Azure.Search.Models.QueryType;
    using SearchParameters = Microsoft.Azure.Search.Models.SearchParameters;

    public class AzureSearchService : ISearchService
    {
        private readonly BatchIndexer _indexer;
        private readonly SearchIndexClient _searchClient;
        private readonly IUrlGenerator _url;
        private readonly IFrameworkCompatibilityService _frameworks;

        public AzureSearchService(
            BatchIndexer indexer,
            SearchIndexClient searchClient,
            IUrlGenerator url,
            IFrameworkCompatibilityService frameworks)
        {
            _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
            _searchClient = searchClient ?? throw new ArgumentNullException(nameof(searchClient));
            _url = url ?? throw new ArgumentNullException(nameof(url));
            _frameworks = frameworks ?? throw new ArgumentNullException(nameof(frameworks));
        }

        public async Task IndexAsync(Package package, CancellationToken cancellationToken)
        {
            await _indexer.IndexAsync(package.Id);
        }

        public async Task<SearchResponse> SearchAsync(
            string query = null,
            int skip = 0,
            int take = 20,
            bool includePrerelease = true,
            bool includeSemVer2 = true,
            CancellationToken cancellationToken = default)
        {
            return await SearchAsync(
                query,
                skip,
                take,
                includePrerelease,
                includeSemVer2,
                packageType: null,
                framework: null,
                cancellationToken);
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
            var searchText = BuildSeachQuery(query, packageType, framework);
            var filter = BuildSearchFilter(includePrerelease, includeSemVer2);
            var parameters = new SearchParameters
            {
                IncludeTotalResultCount = true,
                QueryType = QueryType.Full,
                Skip = skip,
                Top = take,
                Filter = filter
            };

            var response = await _searchClient.Documents.SearchAsync<PackageDocument>(searchText, parameters, cancellationToken: cancellationToken);

            var results = new List<SearchResult>();

            foreach (var result in response.Results)
            {
                var document = result.Document;
                var versions = new List<SearchResultVersion>();

                if (document.Versions.Length != document.VersionDownloads.Length)
                {
                    throw new InvalidOperationException($"Invalid document {document.Key} with mismatched versions");
                }

                for (var i = 0; i < document.Versions.Length; i++)
                {
                    var version = NuGetVersion.Parse(document.Versions[i]);

                    versions.Add(new SearchResultVersion
                    {
                        RegistrationLeafUrl = _url.GetRegistrationLeafUrl(document.Id, version),
                        Version = document.Versions[i],
                        Downloads = long.Parse(document.VersionDownloads[i]),
                    });
                }

                results.Add(new SearchResult
                {
                    PackageId =  document.Id,
                    Version = document.Version,
                    Description = document.Description,
                    Authors = document.Authors,
                    IconUrl = document.IconUrl,
                    LicenseUrl = document.LicenseUrl,
                    ProjectUrl = document.ProjectUrl,
                    RegistrationIndexUrl = _url.GetRegistrationIndexUrl(document.Id),
                    Summary = document.Summary,
                    Tags = document.Tags,
                    Title = document.Title,
                    TotalDownloads = document.TotalDownloads,
                    Versions = versions
                });
            }

            return new SearchResponse
            {
                TotalHits = response.Count.Value,
                Data = results,
                Context = SearchContext.Default(_url.GetPackageMetadataResourceUrl())
            };
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
            // TODO: Do a prefix search on the package id field.
            // TODO: Support versions autocomplete.
            // TODO: Support prerelease and semver2 filters.
            // See: https://github.com/loic-sharma/BaGet/issues/291
            var parameters = new SearchParameters
            {
                IncludeTotalResultCount = true,
                Skip = skip,
                Top = take,
            };

            var response = await _searchClient.Documents.SearchAsync<PackageDocument>(query, parameters, cancellationToken: cancellationToken);
            var results = response.Results
                .Select(r => r.Document.Id)
                .ToList()
                .AsReadOnly();

            return new AutocompleteResponse
            {
                TotalHits = response.Count.Value,
                Data = results,
                Context = AutocompleteContext.Default
            };
        }

        public async Task<DependentsResponse> FindDependentsAsync(
            string packageId,
            int skip = 0,
            int take = 20,
            CancellationToken cancellationToken = default)
        {
            // TODO: Escape packageId.
            var query = $"dependencies:{packageId.ToLowerInvariant()}";
            var parameters = new SearchParameters
            {
                IncludeTotalResultCount = true,
                QueryType = QueryType.Full,
                Skip = skip,
                Top = take,
            };

            var response = await _searchClient.Documents.SearchAsync<PackageDocument>(query, parameters, cancellationToken: cancellationToken);
            var results = response.Results
                .Select(r => r.Document.Id)
                .ToList()
                .AsReadOnly();

            return new DependentsResponse
            {
                TotalHits = response.Count.Value,
                Data = results
            };
        }

        private string BuildSeachQuery(string query, string packageType, string framework)
        {
            var queryBuilder = new StringBuilder();

            if (!string.IsNullOrEmpty(query))
            {
                queryBuilder.Append(query.TrimEnd().TrimEnd('*'));
                queryBuilder.Append('*');
            }

            if (!string.IsNullOrEmpty(packageType))
            {
                queryBuilder.Append(" +packageTypes:");
                queryBuilder.Append(packageType);
            }

            if (!string.IsNullOrEmpty(framework))
            {
                var frameworks = _frameworks.FindAllCompatibleFrameworks(framework);

                queryBuilder.Append(" +frameworks:(");
                queryBuilder.Append(string.Join(" ", frameworks));
                queryBuilder.Append(')');
            }

            return queryBuilder.ToString();
        }

        private string BuildSearchFilter(bool includePrerelease, bool includeSemVer2)
        {
            var searchFilters = SearchFilters.Default;

            if (includePrerelease)
            {
                searchFilters |= SearchFilters.IncludePrerelease;
            }

            if (includeSemVer2)
            {
                searchFilters |= SearchFilters.IncludeSemVer2;
            }

            return $"searchFilters eq '{searchFilters}'";
        }
    }
}
