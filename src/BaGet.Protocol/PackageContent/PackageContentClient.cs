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

        public async Task<Stream> DownloadPackageOrNullAsync(
            string packageId,
            NuGetVersion packageVersion,
            CancellationToken cancellationToken = default)
        {
            var client = await _clientfactory.GetPackageContentClientAsync(cancellationToken);

            return await client.DownloadPackageOrNullAsync(packageId, packageVersion, cancellationToken);
        }

        public async Task<Stream> DownloadPackageManifestOrNullAsync(
            string packageId,
            NuGetVersion packageVersion,
            CancellationToken cancellationToken = default)
        {
            var client = await _clientfactory.GetPackageContentClientAsync(cancellationToken);

            return await client.DownloadPackageManifestOrNullAsync(packageId, packageVersion, cancellationToken);
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