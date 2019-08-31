using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Protocol
{
    /// <summary>
    /// Creates clients to interact with a NuGet server. Use this for advanced scenarios.
    /// For most common scenarios, consider using <see cref="INuGetClient"/> instead.
    /// </summary>
    public interface INuGetClientFactory
    {
        /// <summary>
        /// Create a low level client to interact with the NuGet Service Index resource.
        /// See: https://docs.microsoft.com/en-us/nuget/api/service-index
        /// </summary>
        /// <returns>A client to interact with the NuGet Service Index resource.</returns>
        Task<IServiceIndexResource> CreateServiceIndexClientAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Create a low level client to interact with the NuGet Package Content resource.
        /// See: https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource
        /// </summary>
        /// <returns>A client to interact with the NuGet Package Content resource.</returns>
        Task<IPackageContentResource> CreatePackageContentClientAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Create a low level client to interact with the NuGet Package Metadata resource.
        /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
        /// </summary>
        /// <returns>A client to interact with the NuGet Package Metadata resource.</returns>
        Task<IPackageMetadataResource> CreatePackageMetadataClientAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Create a low level client to interact with the NuGet Search resource.
        /// See: https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource
        /// </summary>
        /// <returns>A client to interact with the NuGet Search resource.</returns>
        Task<ISearchResource> CreateSearchClientAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Create a low level client to interact with the NuGet catalog resource.
        /// See: https://docs.microsoft.com/en-us/nuget/api/catalog-resource
        /// </summary>
        /// <returns>A client to interact with the Catalog resource.</returns>
        Task<ICatalogResource> CreateCatalogClientAsync(CancellationToken cancellationToken = default);
    }
}
