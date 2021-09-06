using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol;
using BaGet.Protocol.Models;
using NuGet.Versioning;
using Microsoft.Extensions.Logging;

namespace BaGet.Core
{
    /// <summary>
    /// The mirroring client for a NuGet server that uses the V3 protocol.
    /// </summary>
    internal sealed class MirrorV3Client : IMirrorClient
    {
        private readonly NuGetClient _client;
        private readonly ILogger<MirrorV3Client> _logger;

        public MirrorV3Client(NuGetClient client, ILogger<MirrorV3Client> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Stream> DownloadPackageAsync(string id, NuGetVersion version,
            CancellationToken cancellationToken)
        {
            return await _client.DownloadPackageAsync(id, version, cancellationToken);
        }

        public async Task<IReadOnlyList<PackageMetadata>> GetPackageMetadataAsync(
            string id,
            CancellationToken cancellationToken)
        {
            try
            {
                return await _client.GetPackageMetadataAsync(id, cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to mirror {PackageId}'s upstream metadata", id);
                return new List<PackageMetadata>();
            }
        }

        public async Task<IReadOnlyList<NuGetVersion>> ListPackageVersionsAsync(
            string id,
            CancellationToken cancellationToken)
        {
            try
            {
                return await _client.ListPackageVersionsAsync(id, includeUnlisted: true, cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to mirror {PackageId}'s upstream versions", id);
                return new List<NuGetVersion>();
            }
        }
    }
}
