using System.Net;
using System.Net.Http;

namespace BaGet.Protocol
{
    /// <summary>
    /// Creates and configures clients that interact with a NuGet server.
    /// </summary>
    public class NuGetClientFactory
    {
        private readonly IServiceIndex _serviceIndexClient;
        private readonly IPackageContentService _packageContentClient;
        private readonly IPackageMetadataService _packageMetadataClient;
        private readonly ISearchService _searchClient;

        /// <summary>
        /// Configure the client factory.
        /// </summary>
        /// <param name="serviceIndexUrl">The NuGet Service Index resource.</param>
        /// <param name="httpClient">The HTTP client used to create NuGet clients.</param>
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

        /// <summary>
        /// Create a client to interact with the NuGet Service Index resource.
        /// See: https://docs.microsoft.com/en-us/nuget/api/service-index
        /// </summary>
        /// <returns>A client to interact with the NuGet Service Index resource.</returns>
        public IServiceIndex CreateServiceIndexClient()
            => _serviceIndexClient;

        /// <summary>
        /// Create a client to interact with the NuGet Package Content resource.
        /// See: https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource
        /// </summary>
        /// <returns>A client to interact with the NuGet Package Content resource.</returns>
        public IPackageContentService CreatePackageContentClient()
            => _packageContentClient;

        /// <summary>
        /// Create a client to interact with the NuGet Package Metadata resource.
        /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
        /// </summary>
        /// <returns>A client to interact with the NuGet Package Metadata resource.</returns>
        public IPackageMetadataService CreatePackageMetadataClient()
            => _packageMetadataClient;

        /// <summary>
        /// Create a client to interact with the NuGet Search resource.
        /// </summary>
        /// <returns>A client to interact with the NuGet Search resource.</returns>
        public ISearchService CreateSearchClient()
            => _searchClient;
    }
}
