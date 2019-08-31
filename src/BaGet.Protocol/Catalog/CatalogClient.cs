using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Protocol.Internal
{
    public class CatalogClient : ICatalogResource
    {
        private readonly HttpClient _httpClient;
        private readonly string _catalogUrl;

        public CatalogClient(HttpClient httpClient, string catalogUrl)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _catalogUrl = catalogUrl ?? throw new ArgumentNullException(nameof(catalogUrl));
        }

        public async Task<CatalogIndex> GetIndexAsync(CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.DeserializeUrlAsync<CatalogIndex>(_catalogUrl, cancellationToken);

            return response.GetResultOrThrow();
        }

        public async Task<CatalogPage> GetPageAsync(string pageUrl, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.DeserializeUrlAsync<CatalogPage>(pageUrl, cancellationToken);

            return response.GetResultOrThrow();
        }

        public async Task<PackageDeleteCatalogLeaf> GetPackageDeleteLeafAsync(string leafUrl, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.DeserializeUrlAsync<PackageDeleteCatalogLeaf>(_catalogUrl, cancellationToken);

            return response.GetResultOrThrow();
        }

        public async Task<PackageDetailsCatalogLeaf> GetPackageDetailsLeafAsync(string leafUrl, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.DeserializeUrlAsync<PackageDetailsCatalogLeaf>(_catalogUrl, cancellationToken);

            return response.GetResultOrThrow();
        }
    }
}
