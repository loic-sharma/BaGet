using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Core.Mirror
{
    /// <summary>
    /// The mirror service used when mirroring has been disabled.
    /// </summary>
    public class FakeMirrorService : IMirrorService
    {
        public Task MirrorAsync(string id, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
