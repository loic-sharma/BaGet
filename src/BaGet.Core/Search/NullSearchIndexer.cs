using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core
{
    public class NullSearchIndexer : ISearchIndexer
    {
        public Task IndexAsync(Package package, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
