using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace BaGet.Core.Mirror
{
    /// <summary>
    /// Indexes packages from an external source.
    /// </summary>
    public interface IMirrorService
    {
        /// <summary>
        /// If the package is unknown, attempt to index it from an upstream source.
        /// </summary>
        /// <param name="id">The package's id</param>
        /// <param name="version">The package's version</param>
        /// <param name="cancellationToken">The token to cancel the mirroring</param>
        /// <returns>A task that completes when the package has been mirrored.</returns>
        Task MirrorAsync(string id, NuGetVersion version, CancellationToken cancellationToken);

        Task<IReadOnlyList<string>> FindUpstreamAsync(string id, CancellationToken ct);

        Task<IEnumerable<IPackageSearchMetadata>> FindUpstreamMetadataAsync(string id, CancellationToken ct);
    }
}
