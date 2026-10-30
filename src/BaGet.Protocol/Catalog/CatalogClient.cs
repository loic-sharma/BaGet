using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;

namespace BaGet.Protocol;

public partial class NuGetClientFactory
{
    private class CatalogClient : ICatalogClient
    {
        private readonly NuGetClientFactory _clientfactory;

        public CatalogClient(NuGetClientFactory clientFactory)
        {
            _clientfactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        }

        public async Task<CatalogIndex> GetIndexAsync(CancellationToken cancellationToken = default)
        {
            var client = await _clientfactory.GetCatalogClientAsync(cancellationToken);

            #pragma warning disable CS8602
            return await client?.GetIndexAsync(cancellationToken);
            #pragma warning restore CS8602
        }

        public async Task<CatalogPage> GetPageAsync(string? pageUrl, CancellationToken cancellationToken = default)
        {
            var client = await _clientfactory.GetCatalogClientAsync(cancellationToken);

            #pragma warning disable CS8602
            return await client?.GetPageAsync(pageUrl, cancellationToken);
            #pragma warning restore CS8602
        }

        public async Task<PackageDetailsCatalogLeaf> GetPackageDetailsLeafAsync(string? leafUrl, CancellationToken cancellationToken = default)
        {
            var client = await _clientfactory.GetCatalogClientAsync(cancellationToken);

            #pragma warning disable CS8602
            return await client?.GetPackageDetailsLeafAsync(leafUrl, cancellationToken);
            #pragma warning restore CS8602
        }

        public async Task<PackageDeleteCatalogLeaf> GetPackageDeleteLeafAsync(string? leafUrl, CancellationToken cancellationToken = default)
        {
            var client = await _clientfactory.GetCatalogClientAsync(cancellationToken);

            #pragma warning disable CS8602
            return await client?.GetPackageDeleteLeafAsync(leafUrl, cancellationToken);
            #pragma warning restore CS8602
        }
    }
}