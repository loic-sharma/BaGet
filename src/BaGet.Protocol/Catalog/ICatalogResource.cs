using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Protocol
{
    /// <summary>
    /// The Catalog resource that records all package operations.
    /// You can use this resource to query for all published packages.
    /// See: https://docs.microsoft.com/en-us/nuget/api/catalog-resource
    /// </summary>
    public interface ICatalogResource
    {
        /// <summary>
        /// Get the entry point for the catalog resource.
        /// See: https://docs.microsoft.com/en-us/nuget/api/catalog-resource#catalog-index
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The catalog index.</returns>
        Task<CatalogIndex> GetIndexAsync(CancellationToken cancellationToken = default);

        Task<CatalogPage> GetPageAsync(
            string pageUrl,
            CancellationToken cancellationToken = default);

        Task<PackageDeleteCatalogLeaf> GetPackageDeleteLeafAsync(
            string leafUrl,
            CancellationToken cancellationToken = default);

        Task<PackageDetailsCatalogLeaf> GetPackageDetailsLeafAsync(
            string leafUrl,
            CancellationToken cancellationToken = default);
    }
}
