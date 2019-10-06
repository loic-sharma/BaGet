using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    public partial class NuGetClientFactory
    {
        private class PackageContentClient : IPackageContentClient
        {
            private readonly NuGetClientFactory _clientfactory;

            public PackageContentClient(NuGetClientFactory clientFactory)
            {
                _clientfactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
            }

            public async Task<Stream> GetPackageContentStreamOrNullAsync(
                string packageId,
                NuGetVersion packageVersion,
                CancellationToken cancellationToken = default)
            {
                var client = await _clientfactory.GetPackageContentClientAsync(cancellationToken);

                return await client.GetPackageContentStreamOrNullAsync(packageId, packageVersion, cancellationToken);
            }

            public async Task<Stream> GetPackageManifestStreamOrNullAsync(
                string packageId,
                NuGetVersion packageVersion,
                CancellationToken cancellationToken = default)
            {
                var client = await _clientfactory.GetPackageContentClientAsync(cancellationToken);

                return await client.GetPackageManifestStreamOrNullAsync(packageId, packageVersion, cancellationToken);
            }

            public async Task<PackageVersionsResponse> GetPackageVersionsOrNullAsync(
                string packageId,
                CancellationToken cancellationToken = default)
            {
                var client = await _clientfactory.GetPackageContentClientAsync(cancellationToken);

                return await client.GetPackageVersionsOrNullAsync(packageId, cancellationToken);
            }
        }
    }
}
