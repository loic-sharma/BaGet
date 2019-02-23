using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core.Services;
using Microsoft.Azure.Search;
using NuGet.Versioning;

namespace BaGet.Azure.Search
{
    using BaGet.Core.Entities;
    using SearchParameters = Microsoft.Azure.Search.Models.SearchParameters;

    public class AzureSearchService : ISearchService
    {
        private readonly BatchIndexer _indexer;
        private readonly SearchIndexClient _searchClient;

        public AzureSearchService(BatchIndexer indexer, SearchIndexClient searchClient)
        {
            _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
            _searchClient = searchClient ?? throw new ArgumentNullException(nameof(searchClient));
        }

        public Task IndexAsync(Package package) => _indexer.IndexAsync(package.Id);

        public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int skip = 0, int take = 20)
        {
            query = query.TrimEnd().TrimEnd('*') + '*';

            var search = await _searchClient.Documents.SearchAsync<PackageDocument>(query, new SearchParameters
            {
                Skip = skip,
                Top = take
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

        public async Task<IReadOnlyList<string>> AutocompleteAsync(string query, int skip = 0, int take = 20)
        {
            var search = await _searchClient.Documents.SearchAsync<PackageDocument>(query);

            return search.Results
                .Select(r => r.Document.Id)
                .ToList()
                .AsReadOnly();
        }

        public Task<IReadOnlyList<string>> FindDependentsAsync(string packageId, int skip = 0, int take = 20)
        {
            throw new NotImplementedException();
        }
    }
}
