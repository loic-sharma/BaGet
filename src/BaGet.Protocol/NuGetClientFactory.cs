using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Internal;

namespace BaGet.Protocol
{
    /// <summary>
    /// Creates clients to interact with a NuGet server. Use this for advanced scenarios.
    /// For most common scenarios, consider using <see cref="NuGetClient"/> instead.
    /// </summary>
    public class NuGetClientFactory : INuGetClientFactory
    {
        // See: https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Protocol/Constants.cs
        private static readonly string Version200 = "/2.0.0";
        private static readonly string Version300beta = "/3.0.0-beta";
        private static readonly string Version300 = "/3.0.0";
        private static readonly string Version340 = "/3.4.0";
        private static readonly string Version360 = "/3.6.0";
        private static readonly string Versioned = "/Versioned";
        private static readonly string Version470 = "/4.7.0";
        private static readonly string Version490 = "/4.9.0";

        private static readonly string[] Catalog = { "Catalog" + Version300 };
        private static readonly string[] SearchQueryService = { "SearchQueryService" + Versioned, "SearchQueryService" + Version340, "SearchQueryService" + Version300beta };
        private static readonly string[] RegistrationsBaseUrl = { "RegistrationsBaseUrl" + Versioned, "RegistrationsBaseUrl" + Version360, "RegistrationsBaseUrl" + Version340, "RegistrationsBaseUrl" + Version300beta };
        private static readonly string[] SearchAutocompleteService = { "SearchAutocompleteService" + Versioned, "SearchAutocompleteService" + Version300beta };
        private static readonly string[] ReportAbuse = { "ReportAbuseUriTemplate" + Versioned, "ReportAbuseUriTemplate" + Version300 };
        private static readonly string[] LegacyGallery = { "LegacyGallery" + Versioned, "LegacyGallery" + Version200 };
        private static readonly string[] PackagePublish = { "PackagePublish" + Versioned, "PackagePublish" + Version200 };
        private static readonly string[] PackageBaseAddress = { "PackageBaseAddress" + Versioned, "PackageBaseAddress" + Version300 };
        private static readonly string[] RepositorySignatures = { "RepositorySignatures" + Version490, "RepositorySignatures" + Version470 };
        private static readonly string[] SymbolPackagePublish = { "SymbolPackagePublish" + Version490 };

        private readonly HttpClient _httpClient;
        private readonly string _serviceIndexUrl;

        private readonly SemaphoreSlim _mutex;
        private NuGetClients _clients;

        public NuGetClientFactory(HttpClient httpClient, string serviceIndexUrl)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _serviceIndexUrl = serviceIndexUrl ?? throw new ArgumentNullException(nameof(serviceIndexUrl));

            _mutex = new SemaphoreSlim(1, 1);
            _clients = null;
        }

        /// <summary>
        /// Create a low level client to interact with the NuGet Service Index resource.
        /// See: https://docs.microsoft.com/en-us/nuget/api/service-index
        /// </summary>
        /// <returns>A client to interact with the NuGet Service Index resource.</returns>
        public Task<IServiceIndexResource> CreateServiceIndexClientAsync(CancellationToken cancellationToken = default)
        {
            return GetClientAsync(c => c.ServiceIndexClient, cancellationToken);
        }

        /// <summary>
        /// Create a low level client to interact with the NuGet Package Content resource.
        /// See: https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource
        /// </summary>
        /// <returns>A client to interact with the NuGet Package Content resource.</returns>
        public Task<IPackageContentResource> CreatePackageContentClientAsync(CancellationToken cancellationToken = default)
        {
            return GetClientAsync(c => c.PackageContentClient, cancellationToken);
        }

        /// <summary>
        /// Create a low level client to interact with the NuGet Package Metadata resource.
        /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
        /// </summary>
        /// <returns>A client to interact with the NuGet Package Metadata resource.</returns>
        public Task<IPackageMetadataResource> CreatePackageMetadataClientAsync(CancellationToken cancellationToken = default)
        {
            return GetClientAsync(c => c.PackageMetadataClient, cancellationToken);
        }

        /// <summary>
        /// Create a low level client to interact with the NuGet Search resource.
        /// See: https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource
        /// </summary>
        /// <returns>A client to interact with the NuGet Search resource.</returns>
        public Task<ISearchResource> CreateSearchClientAsync(CancellationToken cancellationToken = default)
        {
            // TODO: There are multiple search endpoints to support high read availability.
            // This factory should create a search client that uses all these endpoints.
            return GetClientAsync(c => c.SearchClient, cancellationToken);
        }

        /// <summary>
        /// Create a low level client to interact with the NuGet catalog resource.
        /// See: https://docs.microsoft.com/en-us/nuget/api/catalog-resource
        /// </summary>
        /// <returns>A client to interact with the Catalog resource.</returns>
        public Task<ICatalogResource> CreateCatalogClientAsync(CancellationToken cancellationToken = default)
        {
            // TODO: There are multiple search endpoints to support high read availability.
            // This factory should create a search client that uses all these endpoints.
            return GetClientAsync(c => c.CatalogClient, cancellationToken);
        }

        private async Task<T> GetClientAsync<T>(Func<NuGetClients, T> clientFactory, CancellationToken cancellationToken)
        {
            // TODO: This should periodically refresh the service index response.
            if (_clients == null)
            {
                await _mutex.WaitAsync(cancellationToken);

                try
                {
                    if (_clients == null)
                    {
                        var serviceIndexClient = new ServiceIndexClient(_httpClient, _serviceIndexUrl);

                        var serviceIndex = await serviceIndexClient.GetAsync(cancellationToken);

                        var contentClient = new PackageContentClient(_httpClient, GetResourceUrl(serviceIndex, PackageBaseAddress));
                        var metadataClient = new PackageMetadataClient(_httpClient, GetResourceUrl(serviceIndex, RegistrationsBaseUrl));
                        var catalogClient = new CatalogClient(_httpClient, GetResourceUrl(serviceIndex, Catalog));
                        var searchClient = new SearchClient(
                            _httpClient,
                            GetResourceUrl(serviceIndex, SearchQueryService),
                            GetResourceUrl(serviceIndex, SearchAutocompleteService));

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

            return clientFactory(_clients);
        }

        private string GetResourceUrl(ServiceIndexResponse serviceIndex, string[] types)
        {
            var resource = types.SelectMany(t => serviceIndex.Resources.Where(r => r.Type == t)).FirstOrDefault();

            return resource?.ResourceUrl.Trim('/');
        }

        private class NuGetClients
        {
            public IServiceIndexResource ServiceIndexClient { get; set; }
            public IPackageContentResource PackageContentClient { get; set; }
            public IPackageMetadataResource PackageMetadataClient { get; set; }
            public ISearchResource SearchClient { get; set; }
            public ICatalogResource CatalogClient { get; set; }
        }
    }
}
