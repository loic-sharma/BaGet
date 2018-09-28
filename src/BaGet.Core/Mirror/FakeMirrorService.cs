using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace BaGet.Core.Mirror
{
    /// <summary>
    /// The mirror service used when mirroring has been disabled.
    /// </summary>
    public class FakeMirrorService : IMirrorService
    {
        Task<IReadOnlyList<string>> emptyVersions = Task.Factory.StartNew(() => new List<string>() as IReadOnlyList<string>);
        Task<IEnumerable<IPackageSearchMetadata>> emptyMeta = Task.Factory.StartNew(() => new List<IPackageSearchMetadata>() as IEnumerable<IPackageSearchMetadata>);

        public Task<IReadOnlyList<string>> FindUpstreamAsync(string id, CancellationToken ct)
        {
            return emptyVersions;
        }

        public Task<IEnumerable<IPackageSearchMetadata>> FindUpstreamMetadataAsync(string id, CancellationToken ct)
        {
            return emptyMeta;
        }

        public Task MirrorAsync(
            string id,
            NuGetVersion version,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
