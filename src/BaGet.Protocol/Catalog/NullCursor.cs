using System;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Protocol.Catalog
{
    /// <summary>
    /// A cursor that does not persist any state. Use this with a <see cref="CatalogProcessor"/>
    /// to process all leafs each time <see cref="CatalogProcessor.ProcessAsync(CancellationToken)"/>
    /// is called.
    /// </summary>
    public class NullCursor : ICursor
    {
        public Task<DateTimeOffset?> GetAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<DateTimeOffset?>(null);
        }

        public Task SetAsync(DateTimeOffset value, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
