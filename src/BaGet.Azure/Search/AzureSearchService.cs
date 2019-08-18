using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using BaGet.Core.Indexing;
using BaGet.Core.Search;
using BaGet.Core.ServiceIndex;
using BaGet.Protocol;
using Microsoft.Azure.Search;
using NuGet.Versioning;

namespace BaGet.Azure.Search
{
    using QueryType = Microsoft.Azure.Search.Models.QueryType;
    using SearchParameters = Microsoft.Azure.Search.Models.SearchParameters;

    public class AzureSearchService : IBaGetSearchResource
    {
        private readonly BatchIndexer _indexer;
        private readonly SearchIndexClient _searchClient;
        private readonly IBaGetUrlGenerator _url;
        private readonly IFrameworkCompatibilityService _frameworks;

        public AzureSearchService(
            BatchIndexer indexer,
            SearchIndexClient searchClient,
            IBaGetUrlGenerator url,
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

        public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
        {
            return await SearchAsync(BaGetSearchRequest.FromSearchRequest(request), cancellationToken);
        }

        public async Task<SearchResponse> SearchAsync(BaGetSearchRequest request, CancellationToken cancellationToken = default)
        {
            var query = BuildSeachQuery(request);
            var filter = BuildSearchFilter(request);
            var parameters = new SearchParameters
            {
                IncludeTotalResultCount = true,
                QueryType = QueryType.Full,
                Skip = request.Skip,
                Top = request.Take,
                Filter = filter
            };

            var response = await _searchClient.Documents.SearchAsync<PackageDocument>(query, parameters, cancellationToken: cancellationToken);

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
                    var downloads = long.Parse(document.VersionDownloads[i]);
                    var version = NuGetVersion.Parse(document.Versions[i]);
                    var url = _url.GetRegistrationLeafUrl(document.Id, version);

                    versions.Add(new SearchResultVersion(url, version, downloads));
                }

                results.Add(new SearchResult(
                    document.Id,
                    NuGetVersion.Parse(document.Version),
                    document.Description,
                    document.Authors,
                    document.IconUrl,
                    document.LicenseUrl,
                    document.ProjectUrl,
                    _url.GetRegistrationIndexUrl(document.Id),
                    document.Summary,
                    document.Tags,
                    document.Title,
                    document.TotalDownloads,
                    versions));
            }

            return new SearchResponse(
                response.Count.Value,
                results,
                SearchContext.Default(_url.GetPackageMetadataResourceUrl()));
        }

        public async Task<AutocompleteResponse> AutocompleteAsync(AutocompleteRequest request, CancellationToken cancellationToken = default)
        {
            // TODO: Do a prefix search on the package id field.
            // TODO: Support versions autocomplete.
            // See: https://github.com/loic-sharma/BaGet/issues/291
            var parameters = new SearchParameters
            {
                IncludeTotalResultCount = true,
                Skip = request.Skip,
                Top = request.Take,
            };

            var response = await _searchClient.Documents.SearchAsync<PackageDocument>(request.Query, parameters, cancellationToken: cancellationToken);
            var results = response.Results
                .Select(r => r.Document.Id)
                .ToList()
                .AsReadOnly();

            return new AutocompleteResponse(response.Count.Value, results, AutocompleteContext.Default);
        }

        public async Task<DependentsResponse> FindDependentsAsync(DependentsRequest request, CancellationToken cancellationToken = default)
        {
            // TODO: Escape packageId.
            var query = $"dependencies:{request.PackageId.ToLowerInvariant()}";
            var parameters = new SearchParameters
            {
                IncludeTotalResultCount = true,
                QueryType = QueryType.Full,
                Skip = request.Skip,
                Top = request.Take,
            };

            var response = await _searchClient.Documents.SearchAsync<PackageDocument>(query, parameters, cancellationToken: cancellationToken);
            var results = response.Results
                .Select(r => r.Document.Id)
                .ToList()
                .AsReadOnly();

            return new DependentsResponse(response.Count.Value, results);
        }

        private string BuildSeachQuery(BaGetSearchRequest request)
        {
            var query = string.Empty;
            if (!string.IsNullOrEmpty(request.Query))
            {
                query = request.Query.TrimEnd().TrimEnd('*') + '*';
            }

            if (!string.IsNullOrEmpty(request.PackageType))
            {
                query = $"+packageTypes:{request.PackageType} {query}";
            }

            if (!string.IsNullOrEmpty(request.Framework))
            {
                var frameworks = _frameworks.FindAllCompatibleFrameworks(request.Framework);

                query = $"+frameworks:({string.Join(" ", frameworks)}) {query}";
            }

            return query;
        }

        private string BuildSearchFilter(BaGetSearchRequest request)
        {
            var searchFilters = SearchFilters.Default;

            if (request.IncludePrerelease)
            {
                searchFilters |= SearchFilters.IncludePrerelease;
            }

            if (request.IncludeSemVer2)
            {
                searchFilters |= SearchFilters.IncludeSemVer2;
            }

            return $"searchFilters eq '{searchFilters}'";
        }
    }
}
