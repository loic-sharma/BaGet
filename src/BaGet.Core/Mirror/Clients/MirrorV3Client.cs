using BaGet.Protocol;
using BaGet.Protocol.Models;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core
{
    internal sealed class MirrorV3Client : IMirrorNuGetClient
    {
        private readonly NuGetClient _client;

        public MirrorV3Client(NuGetClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public Task<Stream> DownloadPackageAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        {
            return _client.DownloadPackageAsync(id, version, cancellationToken);
        }

        public Task<IReadOnlyList<PackageMetadata>> GetPackageMetadataAsync(string id, CancellationToken cancellationToken)
        {
            return _client.GetPackageMetadataAsync(id, cancellationToken);
        }

        public Task<IReadOnlyList<NuGetVersion>> ListPackageVersionsAsync(string id, bool includeUnlisted, CancellationToken cancellationToken)
        {
            return _client.ListPackageVersionsAsync(id, includeUnlisted, cancellationToken);
        }
    }
}
