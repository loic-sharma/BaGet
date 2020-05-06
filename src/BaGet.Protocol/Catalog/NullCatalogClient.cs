using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;

namespace BaGet.Protocol.Internal
{
    public class NullCatalogClient : ICatalogClient
    {
        public Task<CatalogIndex> GetIndexAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CatalogIndex
            {
                CommitTimestamp = DateTimeOffset.MinValue,
                Count = 0,
                Items = new List<CatalogPageItem>()
            });
        }

        public Task<CatalogPage> GetPageAsync(string pageUrl, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException($"{nameof(NullCatalogClient)} does not support loading catalog pages.");
        }

        public Task<PackageDeleteCatalogLeaf> GetPackageDeleteLeafAsync(string leafUrl, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException($"{nameof(NullCatalogClient)} does not support loading catalog leaves.");
        }

        public Task<PackageDetailsCatalogLeaf> GetPackageDetailsLeafAsync(string leafUrl, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException($"{nameof(NullCatalogClient)} does not support loading catalog leaves.");
        }
    }
}
