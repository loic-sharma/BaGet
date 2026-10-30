using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;
using NuGet.Versioning;

namespace BaGet.Protocol;

public partial class NuGetClientFactory
{
    private class PackageContentClient : IPackageContentClient
    {
        private readonly NuGetClientFactory _clientfactory;

        public PackageContentClient(NuGetClientFactory clientFactory)
        {
            _clientfactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        }

        public async Task<Stream?> DownloadPackageOrNullAsync(
            string? packageId,
            NuGetVersion packageVersion,
            CancellationToken cancellationToken = default)
        {
            var client = await _clientfactory.GetPackageContentClientAsync(cancellationToken);

            #pragma warning disable CS8602
            return await client?.DownloadPackageOrNullAsync(packageId, packageVersion, cancellationToken);
            #pragma warning restore CS8602
        }

        public async Task<Stream?> DownloadPackageManifestOrNullAsync(
            string packageId,
            NuGetVersion packageVersion,
            CancellationToken cancellationToken = default)
        {
            var client = await _clientfactory.GetPackageContentClientAsync(cancellationToken);

            #pragma warning disable CS8602
            return await client?.DownloadPackageManifestOrNullAsync(packageId, packageVersion, cancellationToken);
            #pragma warning restore CS8602
        }

        public async Task<PackageVersionsResponse> GetPackageVersionsOrNullAsync(
            string packageId,
            CancellationToken cancellationToken = default)
        {
            var client = await _clientfactory.GetPackageContentClientAsync(cancellationToken);

            #pragma warning disable CS8602
            return await client?.GetPackageVersionsOrNullAsync(packageId, cancellationToken);
            #pragma warning restore CS8602
        }
    }
}