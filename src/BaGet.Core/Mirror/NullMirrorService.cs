using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Core
{
    /// <summary>
    /// The mirror service used when mirroring has been disabled.
    /// </summary>
    public class NullMirrorService : IMirrorService
    {
        public Task<IReadOnlyList<NuGetVersion>> FindPackageVersionsOrNullAsync(string id, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<NuGetVersion>>(null);
        }

        public Task<IReadOnlyList<Package>> FindPackagesOrNullAsync(string id, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<Package>>(null);
        }

        public Task MirrorAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
