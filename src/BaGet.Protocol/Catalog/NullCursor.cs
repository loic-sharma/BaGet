using System;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Protocol.Catalog
{
    /// <summary>
    /// A cursor that does not persist any state.
    /// </summary>
    public class NullCursor : ICursor
    {
        public Task<DateTimeOffset?> GetAsync(CancellationToken cancellationToken = default)
        {
            return null;
        }

        public Task SetAsync(DateTimeOffset value, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
