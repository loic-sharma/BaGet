using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using NuGet.Versioning;

namespace BaGet.Core.Mirror
{
    /// <summary>
    /// The mirror service used when mirroring has been disabled.
    /// </summary>
    public class FakeMirrorService : IMirrorService
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
