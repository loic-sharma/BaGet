using System.Net;
using System.Net.Http;

namespace BaGet.Protocol
{
    public class NuGetClientFactory
    {
        private readonly IServiceIndex _serviceIndexClient;
        private readonly IPackageContentService _packageContentClient;
        private readonly IPackageMetadataService _packageMetadataClient;
        private readonly ISearchService _searchClient;

        public NuGetClientFactory(string serviceIndexUrl, HttpClient httpClient = null)
        {
            httpClient = httpClient ?? new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            });

            _serviceIndexClient = new ServiceIndexClient(httpClient, serviceIndexUrl);

            var urlGeneratorFactory = new UrlGeneratorClientFactory(_serviceIndexClient);

            _packageContentClient = new PackageContentClient(urlGeneratorFactory, httpClient);
            _packageMetadataClient = new PackageMetadataClient(urlGeneratorFactory, httpClient);
            _searchClient = new SearchClient(urlGeneratorFactory, httpClient);
        }

        public IServiceIndex CreateServiceIndexClient()
            => _serviceIndexClient;

        public IPackageContentService CreatePackageContentClient()
            => _packageContentClient;

        public IPackageMetadataService CreatePackageMetadataClient()
            => _packageMetadataClient;

        public ISearchService CreateSearchClient()
            => _searchClient;
    }
}
