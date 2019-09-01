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
            return await GetAndValidateLeafAsync<PackageDeleteCatalogLeaf>(
                CatalogLeafType.PackageDelete,
                leafUrl,
                cancellationToken);
        }

        public async Task<PackageDetailsCatalogLeaf> GetPackageDetailsLeafAsync(string leafUrl, CancellationToken cancellationToken = default)
        {
            return await GetAndValidateLeafAsync<PackageDetailsCatalogLeaf>(
                CatalogLeafType.PackageDetails,
                leafUrl,
                cancellationToken);
        }

        private async Task<T> GetAndValidateLeafAsync<T>(
            CatalogLeafType type,
            string leafUrl,
            CancellationToken cancellationToken) where T : CatalogLeaf
        {
            var result = await _httpClient.DeserializeUrlAsync<T>(leafUrl, cancellationToken);
            var leaf = result.GetResultOrThrow();

            if (leaf.Type != type)
            {
                throw new ArgumentException(
                    $"The leaf type found in the document does not match the expected '{type}' type.",
                    nameof(type));
            }

            return leaf;
        }
    }
}
