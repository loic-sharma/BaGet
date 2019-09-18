using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Internal;

namespace BaGet.Protocol
{
    /// <summary>
    /// The <see cref="NuGetClientFactory"/> creates clients to interact with a NuGet server.
    /// Use this for advanced scenarios. For most scenarios, consider using <see cref="NuGetClient"/> instead.
    /// </summary>
    public class NuGetClientFactory
    {
        private readonly HttpClient _httpClient;
        private readonly string _serviceIndexUrl;

        private readonly SemaphoreSlim _mutex;
        private NuGetClients _clients;

        /// <summary>
        /// Initializes a new instance of the <see cref="NuGetClientFactory"/> class
        /// for mocking.
        /// </summary>
        protected NuGetClientFactory()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NuGetClientFactory"/> class.
        /// </summary>
        /// <param name="httpClient">The client used for HTTP requests.</param>
        /// <param name="serviceIndexUrl">
        /// The NuGet Service Index resource URL.
        ///
        /// For NuGet.org, use https://api.nuget.org/v3/index.json
        /// </param>
        public NuGetClientFactory(HttpClient httpClient, string serviceIndexUrl)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _serviceIndexUrl = serviceIndexUrl ?? throw new ArgumentNullException(nameof(serviceIndexUrl));

            _mutex = new SemaphoreSlim(1, 1);
            _clients = null;
        }

        /// <summary>
        /// Create a client to interact with the NuGet Service Index resource.
        /// 
        /// See https://docs.microsoft.com/en-us/nuget/api/service-index
        /// </summary>
        /// <returns>A client to interact with the NuGet Service Index resource.</returns>
        public virtual Task<IServiceIndexClient> CreateServiceIndexClientAsync(CancellationToken cancellationToken = default)
        {
            return GetClientAsync(c => c.ServiceIndexClient, cancellationToken);
        }

        /// <summary>
        /// Create a client to interact with the NuGet Package Content resource.
        ///
        /// See https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource
        /// </summary>
        /// <returns>A client to interact with the NuGet Package Content resource.</returns>
        public virtual Task<IPackageContentClient> CreatePackageContentClientAsync(CancellationToken cancellationToken = default)
        {
            return GetClientAsync(c => c.PackageContentClient, cancellationToken);
        }

        /// <summary>
        /// Create a client to interact with the NuGet Package Metadata resource.
        /// 
        /// See https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
        /// </summary>
        /// <returns>A client to interact with the NuGet Package Metadata resource.</returns>
        public virtual Task<IPackageMetadataClient> CreatePackageMetadataClientAsync(CancellationToken cancellationToken = default)
        {
            return GetClientAsync(c => c.PackageMetadataClient, cancellationToken);
        }

        /// <summary>
        /// Create a client to interact with the NuGet Search resource.
        /// 
        /// See https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource
        /// </summary>
        /// <returns>A client to interact with the NuGet Search resource.</returns>
        public virtual Task<ISearchClient> CreateSearchClientAsync(CancellationToken cancellationToken = default)
        {
            return GetClientAsync(c => c.SearchClient, cancellationToken);
        }

        /// <summary>
        /// Create a client to interact with the NuGet catalog resource.
        /// 
        /// See https://docs.microsoft.com/en-us/nuget/api/catalog-resource
        /// </summary>
        /// <returns>A client to interact with the Catalog resource.</returns>
        public virtual Task<ICatalogClient> CreateCatalogClientAsync(CancellationToken cancellationToken = default)
        {
            return GetClientAsync(c => c.CatalogClient, cancellationToken);
        }

        private async Task<T> GetClientAsync<T>(Func<NuGetClients, T> clientFactory, CancellationToken cancellationToken)
        {
            if (_clients == null)
            {
                await _mutex.WaitAsync(cancellationToken);

                try
                {
                    if (_clients == null)
                    {
                        var serviceIndexClient = new ServiceIndexClient(_httpClient, _serviceIndexUrl);

                        var serviceIndex = await serviceIndexClient.GetAsync(cancellationToken);

                        var contentClient = new RawPackageContentClient(_httpClient, serviceIndex.GetPackageContentResourceUrl());
                        var metadataClient = new RawPackageMetadataClient(_httpClient, serviceIndex.GetPackageMetadataResourceUrl());
                        var catalogClient = new RawCatalogClient(_httpClient, serviceIndex.GetCatalogResourceUrl());
                        var searchClient = new RawSearchClient(_httpClient,
                            serviceIndex.GetSearchQueryResourceUrl(),
                            serviceIndex.GetSearchAutocompleteResourceUrl());

                        _clients = new NuGetClients
                        {
                            ServiceIndexClient = serviceIndexClient,
                            PackageContentClient = contentClient,
                            PackageMetadataClient = metadataClient,
                            SearchClient = searchClient,
                            CatalogClient = catalogClient,
                        };
                    }
                }
                finally
                {
                    _mutex.Release();
                }
            }

            // TODO: This should periodically refresh the service index response.
            return clientFactory(_clients);
        }

        private class NuGetClients
        {
            public IServiceIndexClient ServiceIndexClient { get; set; }
            public IPackageContentClient PackageContentClient { get; set; }
            public IPackageMetadataClient PackageMetadataClient { get; set; }
            public ISearchClient SearchClient { get; set; }
            public ICatalogClient CatalogClient { get; set; }
        }
    }
}
