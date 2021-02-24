using BaGet.Protocol.Models;
using NuGet.Versioning;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core
{
    public interface IMirrorNuGetClient
    {
        Task<IReadOnlyList<NuGetVersion>> ListPackageVersionsAsync(string id, bool includeUnlisted, CancellationToken cancellationToken);
        Task<IReadOnlyList<PackageMetadata>> GetPackageMetadataAsync(string id, CancellationToken cancellationToken);
        Task<Stream> DownloadPackageAsync(string id, NuGetVersion version, CancellationToken cancellationToken);
    }
}
