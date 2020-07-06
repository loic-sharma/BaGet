using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;

namespace BaGet.Protocol.Internal
{
    public class RawCatalogClient : ICatalogClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _catalogUrl;

        public RawCatalogClient(HttpClient httpClient, string catalogUrl)
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
                "PackageDelete",
                leafUrl,
                cancellationToken);
        }

        public async Task<PackageDetailsCatalogLeaf> GetPackageDetailsLeafAsync(string leafUrl, CancellationToken cancellationToken = default)
        {
            return await GetAndValidateLeafAsync<PackageDetailsCatalogLeaf>(
                "PackageDetails",
                leafUrl,
                cancellationToken);
        }

        private async Task<TCatalogLeaf> GetAndValidateLeafAsync<TCatalogLeaf>(
            string leafType,
            string leafUrl,
            CancellationToken cancellationToken) where TCatalogLeaf : CatalogLeaf
        {
            var result = await _httpClient.DeserializeUrlAsync<TCatalogLeaf>(leafUrl, cancellationToken);
            var leaf = result.GetResultOrThrow();

            if (leaf.Type.FirstOrDefault() != leafType)
            {
                throw new ArgumentException(
                    $"The leaf type found in the document does not match the expected '{leafType}' type.",
                    nameof(leafType));
            }

            return leaf;
        }
    }
}
