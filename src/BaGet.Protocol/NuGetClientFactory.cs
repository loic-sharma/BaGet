using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Internal;
using BaGet.Protocol.Models;

namespace BaGet.Protocol;

/// <summary>
/// The <see cref="NuGetClientFactory"/> creates clients to interact with a NuGet server.
/// Use this for advanced scenarios. For most scenarios, consider using <see cref="NuGetClient"/> instead.
/// </summary>
public partial class NuGetClientFactory
{
    /// <summary>
    /// How long a service index response is reused before it is fetched again. A long-lived
    /// factory therefore picks up resources the server adds, moves or removes, without
    /// re-fetching the index on every request. NuGet's own client caches the service index
    /// for the same reason and for the same duration; see NuGet.Protocol's
    /// ServiceIndexResourceV3Provider.MaxCacheDuration.
    /// </summary>
    internal static readonly TimeSpan DefaultServiceIndexCacheDuration = TimeSpan.FromMinutes(40);

    private readonly HttpClient? _httpClient;
    private readonly string? _serviceIndexUrl;

    private readonly TimeSpan _serviceIndexCacheDuration = DefaultServiceIndexCacheDuration;
    private readonly Func<DateTimeOffset> _clock = () => DateTimeOffset.UtcNow;

    private readonly SemaphoreSlim? _mutex;
    private volatile NuGetClients? _clients;

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
    public NuGetClientFactory(HttpClient? httpClient, string? serviceIndexUrl)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _serviceIndexUrl = serviceIndexUrl ?? throw new ArgumentNullException(nameof(serviceIndexUrl));

        _mutex = new SemaphoreSlim(1, 1);
        _clients = null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NuGetClientFactory"/> class with a
    /// service index cache duration and a clock of its own, so that the expiry can be tested.
    /// </summary>
    internal NuGetClientFactory(
        HttpClient httpClient,
        string serviceIndexUrl,
        TimeSpan serviceIndexCacheDuration,
        Func<DateTimeOffset> clock)
        : this(httpClient, serviceIndexUrl)
    {
        if (serviceIndexCacheDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(serviceIndexCacheDuration));
        }

        _serviceIndexCacheDuration = serviceIndexCacheDuration;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>
    /// Create a client to interact with the NuGet Service Index resource.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/service-index
    /// </summary>
    /// <returns>A client to interact with the NuGet Service Index resource.</returns>
    public virtual IServiceIndexClient CreateServiceIndexClient()
    {
        return new ServiceIndexClient(this);
    }

    /// <summary>
    /// Create a client to interact with the NuGet Package Content resource.
    ///
    /// See https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource
    /// </summary>
    /// <returns>A client to interact with the NuGet Package Content resource.</returns>
    public virtual IPackageContentClient CreatePackageContentClient()
    {
        return new PackageContentClient(this);
    }

    /// <summary>
    /// Create a client to interact with the NuGet Package Metadata resource.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
    /// </summary>
    /// <returns>A client to interact with the NuGet Package Metadata resource.</returns>
    public virtual IPackageMetadataClient CreatePackageMetadataClient()
    {
        return new PackageMetadataClient(this);
    }

    /// <summary>
    /// Create a client to interact with the NuGet Search resource.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource
    /// </summary>
    /// <returns>A client to interact with the NuGet Search resource.</returns>
    public virtual ISearchClient CreateSearchClient()
    {
        return new SearchClient(this);
    }

    /// <summary>
    /// Create a client to interact with the NuGet Autocomplete resource.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/search-autocomplete-service-resource
    /// </summary>
    /// <returns>A client to interact with the NuGet Autocomplete resource.</returns>
    public virtual IAutocompleteClient CreateAutocompleteClient()
    {
        return new AutocompleteClient(this);
    }

    /// <summary>
    /// Create a client to interact with the NuGet catalog resource.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/catalog-resource
    /// </summary>
    /// <returns>A client to interact with the Catalog resource.</returns>
    public virtual ICatalogClient CreateCatalogClient()
    {
        return new CatalogClient(this);
    }


    private Task<ServiceIndexResponse?> GetServiceIndexAsync(CancellationToken cancellationToken = default)
    {
        return GetAsync(c => c.ServiceIndex, cancellationToken);
    }

    private Task<IPackageContentClient?> GetPackageContentClientAsync(CancellationToken cancellationToken = default)
    {
        return GetAsync(c => c.PackageContentClient, cancellationToken);
    }

    private Task<IPackageMetadataClient?> GetPackageMetadataClientAsync(CancellationToken cancellationToken = default)
    {
        return GetAsync(c => c.PackageMetadataClient, cancellationToken);
    }

    private Task<IReadOnlyList<ISearchClient>?> GetSearchClientsAsync(CancellationToken cancellationToken = default)
    {
        return GetAsync(c => c.SearchClients, cancellationToken);
    }

    private Task<IReadOnlyList<IAutocompleteClient>?> GetAutocompleteClientsAsync(CancellationToken cancellationToken = default)
    {
        return GetAsync(c => c.AutocompleteClients, cancellationToken);
    }

    private Task<ICatalogClient?> GetCatalogClientAsync(CancellationToken cancellationToken = default)
    {
        return GetAsync(c => c.CatalogClient, cancellationToken);
    }

    private async Task<T> GetAsync<T>(Func<NuGetClients, T> clientFactory, CancellationToken cancellationToken)
    {
        var clients = _clients;

        if (clients == null || _clock() >= clients.ExpiresAt)
        {
            #pragma warning disable CS8602
            await _mutex?.WaitAsync(cancellationToken);
            #pragma warning restore CS8602

            try
            {
                // Another caller may have refreshed the service index while this one waited.
                if (_clients == null || _clock() >= _clients.ExpiresAt)
                {
                    _clients = await BuildClientsAsync(cancellationToken);
                }

                clients = _clients;
            }
            finally
            {
                _mutex?.Release();
            }
        }

        return clientFactory(clients);
    }

    private async Task<NuGetClients> BuildClientsAsync(CancellationToken cancellationToken)
    {
        var serviceIndexClient = new RawServiceIndexClient(_httpClient, _serviceIndexUrl);

        var serviceIndex = await serviceIndexClient.GetAsync(cancellationToken);

        var contentResourceUrl = serviceIndex.GetPackageContentResourceUrl();
        var metadataResourceUrl = serviceIndex.GetPackageMetadataResourceUrl();
        var catalogResourceUrl = serviceIndex.GetCatalogResourceUrl();

        // The service index may advertise several endpoints for the search and autocomplete
        // resources. Keep one client per endpoint, in the order they should be tried.
        var searchResourceUrls = serviceIndex.GetSearchQueryResourceUrls();
        var autocompleteResourceUrls = serviceIndex.GetSearchAutocompleteResourceUrls();

        // Create clients for required resources.
        var contentClient = new RawPackageContentClient(_httpClient, contentResourceUrl);
        var metadataClient = new RawPackageMetadataClient(_httpClient, metadataResourceUrl);
        var searchClients = searchResourceUrls
            .Select(url => (ISearchClient)new RawSearchClient(_httpClient, url))
            .ToList();

        // Create clients for optional resources.
        var catalogClient = catalogResourceUrl == null
            ? new NullCatalogClient() as ICatalogClient
            : new RawCatalogClient(_httpClient, catalogResourceUrl);
        var autocompleteClients = autocompleteResourceUrls.Count == 0
            ? new List<IAutocompleteClient> { new NullAutocompleteClient() }
            : autocompleteResourceUrls
                .Select(url => (IAutocompleteClient)new RawAutocompleteClient(_httpClient, url))
                .ToList();

        return new NuGetClients
        {
            ServiceIndex = serviceIndex,
            ExpiresAt = _clock() + _serviceIndexCacheDuration,

            PackageContentClient = contentClient,
            PackageMetadataClient = metadataClient,
            SearchClients = searchClients,
            AutocompleteClients = autocompleteClients,
            CatalogClient = catalogClient,
        };
    }

    private class NuGetClients
    {
        public ServiceIndexResponse? ServiceIndex { get; set; }

        /// <summary>
        /// When these clients stop being used, and the service index is fetched again.
        /// It lives on the snapshot so that reading it needs no synchronization.
        /// </summary>
        public DateTimeOffset ExpiresAt { get; set; }

        public IPackageContentClient? PackageContentClient { get; set; }
        public IPackageMetadataClient? PackageMetadataClient { get; set; }
        public IReadOnlyList<ISearchClient>? SearchClients { get; set; }
        public IReadOnlyList<IAutocompleteClient>? AutocompleteClients { get; set; }
        public ICatalogClient? CatalogClient { get; set; }
    }
}