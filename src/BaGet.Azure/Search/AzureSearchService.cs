using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using BaGet.Core.Indexing;
using BaGet.Core.Search;
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
        private readonly IFrameworkCompatibilityService _frameworks;

        public AzureSearchService(
            BatchIndexer indexer,
            SearchIndexClient searchClient,
            IFrameworkCompatibilityService frameworks)
        {
            _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
            _searchClient = searchClient ?? throw new ArgumentNullException(nameof(searchClient));
            _frameworks = frameworks ?? throw new ArgumentNullException(nameof(frameworks));
        }

        public Task IndexAsync(Package package) => _indexer.IndexAsync(package.Id);

        public async Task<IReadOnlyList<SearchResult>> SearchAsync(
            string query,
            int skip = 0,
            int take = 20,
            bool includePrerelease = true,
            bool includeSemVer2 = true,
            string packageType = null,
            string framework = null)
        {
            query = BuildSeachQuery(query, packageType, framework);

            var searchFilters = SearchFilters.Default;

            if (includePrerelease)
            {
                searchFilters |= SearchFilters.IncludePrerelease;
            }

            if (includeSemVer2)
            {
                searchFilters |= SearchFilters.IncludeSemVer2;
            }

            var search = await _searchClient.Documents.SearchAsync<PackageDocument>(query, new SearchParameters
            {
                QueryType = QueryType.Full,
                Skip = skip,
                Top = take,
                Filter = $"searchFilters eq '{searchFilters}'"
            });

            var results = new List<SearchResult>();

            foreach (var result in search.Results)
            {
                var document = result.Document;
                var versions = new List<SearchResultVersion>();

                if (document.Versions.Length != document.VersionDownloads.Length)
                {
                    throw new InvalidOperationException($"Invalid document {document.Key} with mismatched versions");
                }

                for (var i = 0; i < document.Versions.Length; i++)
                {
                    versions.Add(new SearchResultVersion(
                        NuGetVersion.Parse(document.Versions[i]),
                        long.Parse(document.VersionDownloads[i])));
                }

                results.Add(new SearchResult
                {
                    Id = document.Id,
                    Version = NuGetVersion.Parse(document.Version),
                    Description = document.Description,
                    Authors = document.Authors,
                    IconUrl = document.IconUrl,
                    LicenseUrl = document.LicenseUrl,
                    Summary = document.Summary,
                    Tags = document.Tags,
                    Title = document.Title,
                    TotalDownloads = document.TotalDownloads,
                    Versions = versions.AsReadOnly()
                });
            }

            return results.AsReadOnly();
        }

        private string BuildSeachQuery(string query, string packageType, string framework)
        {
            if (!string.IsNullOrEmpty(query))
            {
                query = query.TrimEnd().TrimEnd('*') + '*';
            }

            if (!string.IsNullOrEmpty(packageType))
            {
                query = $"+packageTypes:{packageType} {query}";
            }

            if (!string.IsNullOrEmpty(framework))
            {
                var frameworks = _frameworks.FindAllCompatibleFrameworks(framework);

                query = $"+frameworks:({string.Join(" ", frameworks)}) {query}";
            }

            return query;
        }

        public async Task<IReadOnlyList<string>> AutocompleteAsync(string query, int skip = 0, int take = 20)
        {
            var search = await _searchClient.Documents.SearchAsync<PackageDocument>(query, new SearchParameters
            {
                Skip = skip,
                Top = take,
            });

            return search.Results
                .Select(r => r.Document.Id)
                .ToList()
                .AsReadOnly();
        }

        public async Task<IReadOnlyList<string>> FindDependentsAsync(string packageId, int skip = 0, int take = 20)
        {
            // TODO: Escape packageId.
            var query = $"dependencies:{packageId.ToLowerInvariant()}";
            var search = await _searchClient.Documents.SearchAsync<PackageDocument>(query, new SearchParameters
            {
                QueryType = QueryType.Full,
                Skip = skip,
                Top = take,
            });

            return search.Results
                .Select(r => r.Document.Id)
                .ToList()
                .AsReadOnly();
        }
    }
}
