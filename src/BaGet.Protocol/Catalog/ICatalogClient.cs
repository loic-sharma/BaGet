using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;

namespace BaGet.Protocol
{
    /// <summary>
    /// The Catalog client, used to discover package events.
    /// You can use this resource to query for all published packages.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/catalog-resource
    /// </summary>
    public interface ICatalogClient
    {
        /// <summary>
        /// Get the entry point for the catalog resource.
        /// See: https://docs.microsoft.com/en-us/nuget/api/catalog-resource#catalog-index
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The catalog index.</returns>
        Task<CatalogIndex> GetIndexAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Get a single catalog page, used to discover catalog leafs.
        /// See: https://docs.microsoft.com/en-us/nuget/api/catalog-resource#catalog-page
        /// </summary>
        /// <param name="pageUrl">The URL of the page, from the <see cref="CatalogIndex"/>.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>A catalog page.</returns>
        Task<CatalogPage> GetPageAsync(
            string pageUrl,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Get a single catalog leaf, representing a package deletion event.
        /// See: https://docs.microsoft.com/en-us/nuget/api/catalog-resource#catalog-leaf
        /// </summary>
        /// <param name="leafUrl">The URL of the leaf, from a <see cref="CatalogPage"/>.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>A catalog leaf.</returns>
        Task<PackageDeleteCatalogLeaf> GetPackageDeleteLeafAsync(
            string leafUrl,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Get a single catalog leaf, representing a package creation or update event.
        /// See: https://docs.microsoft.com/en-us/nuget/api/catalog-resource#catalog-leaf
        /// </summary>
        /// <param name="leafUrl">The URL of the leaf, from a <see cref="CatalogPage"/>.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>A catalog leaf.</returns>
        Task<PackageDetailsCatalogLeaf> GetPackageDetailsLeafAsync(
            string leafUrl,
            CancellationToken cancellationToken = default);
    }
}
