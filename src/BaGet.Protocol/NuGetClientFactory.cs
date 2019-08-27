using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Internal;

namespace BaGet.Protocol
{
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
            // TODO: Integrate with HttpClientFactory?
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _serviceIndexUrl = serviceIndexUrl ?? throw new ArgumentNullException(nameof(serviceIndexUrl));

            _mutex = new SemaphoreSlim(1, 1);
            _clients = null;
        }
        public Task<IServiceIndexResource> CreateServiceIndexClientAsync(CancellationToken cancellationToken = default)
        {
            return GetClientAsync(c => c.ServiceIndexClient, cancellationToken);
        }

        public Task<IPackageContentResource> CreatePackageContentClientAsync(CancellationToken cancellationToken = default)
        {
            return GetClientAsync(c => c.PackageContentClient, cancellationToken);
        }

        public Task<IPackageMetadataResource> CreatePackageMetadataClientAsync(CancellationToken cancellationToken = default)
        {
            return GetClientAsync(c => c.PackageMetadataClient, cancellationToken);
        }

        public Task<ISearchResource> CreateSearchClientAsync(CancellationToken cancellationToken = default)
        {
            return GetClientAsync(c => c.SearchClient, cancellationToken);
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
                        var searchClient = new SearchClient(
                            _httpClient,
                            GetResourceUrl(serviceIndex, SearchQueryService),
                            GetResourceUrl(serviceIndex, SearchAutocompleteService));

                        _clients = new NuGetClients
                        {
                            ServiceIndexClient = serviceIndexClient,
                            PackageContentClient = contentClient,
                            PackageMetadataClient = null,
                            SearchClient = null,
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

            return resource?.Url.Trim('/');
        }

        private class NuGetClients
        {
            public IServiceIndexResource ServiceIndexClient { get; set; }
            public IPackageContentResource PackageContentClient { get; set; }
            public IPackageMetadataResource PackageMetadataClient { get; set; }
            public ISearchResource SearchClient { get; set; }
        }
    }
}
