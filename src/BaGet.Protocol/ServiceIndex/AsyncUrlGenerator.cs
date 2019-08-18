using System;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    internal class AsyncUrlGenerator : IAsyncUrlGenerator
    {
        private readonly IServiceIndexResource _serviceIndex;
        private readonly SemaphoreSlim _mutex;
        private UrlGeneratorClient _urlGenerator;

        public AsyncUrlGenerator(IServiceIndexResource serviceIndex)
        {
            _serviceIndex = serviceIndex ?? throw new ArgumentNullException(nameof(serviceIndex));
            _mutex = new SemaphoreSlim(1, 1);

            _urlGenerator = null;
        }

        public Task<string> GetPackageContentResourceUrlAsync(CancellationToken cancellationToken = default)
            => GetUrlAsync(
                g => g.GetPackageContentResourceUrl(),
                cancellationToken);

        public Task<string> GetPackageMetadataResourceUrlAsync(CancellationToken cancellationToken = default)
            => GetUrlAsync(
                g => g.GetPackageMetadataResourceUrl(),
                cancellationToken);

        public Task<string> GetPackagePublishResourceUrlAsync(CancellationToken cancellationToken = default)
            => GetUrlAsync(
                g => g.GetPackagePublishResourceUrl(),
                cancellationToken);

        public Task<string> GetSymbolPublishResourceUrlAsync(CancellationToken cancellationToken = default)
            => GetUrlAsync(
                g => g.GetSymbolPublishResourceUrl(),
                cancellationToken);

        public Task<string> GetSearchResourceUrlAsync(CancellationToken cancellationToken = default)
            => GetUrlAsync(
                g => g.GetSearchResourceUrl(),
                cancellationToken);

        public Task<string> GetAutocompleteResourceUrlAsync(CancellationToken cancellationToken = default)
            => GetUrlAsync(
                g => g.GetAutocompleteResourceUrl(),
                cancellationToken);

        public Task<string> GetRegistrationIndexUrlAsync(string id, CancellationToken cancellationToken = default)
            => GetUrlAsync(
                g => g.GetRegistrationIndexUrl(id),
                cancellationToken);

        public Task<string> GetRegistrationPageUrlAsync(string id, NuGetVersion lower, NuGetVersion upper, CancellationToken cancellationToken = default)
            => GetUrlAsync(
                g => g.GetRegistrationPageUrl(id, lower, upper),
                cancellationToken);

        public Task<string> GetRegistrationLeafUrlAsync(string id, NuGetVersion version, CancellationToken cancellationToken = default)
            => GetUrlAsync(
                g => g.GetRegistrationLeafUrl(id, version),
                cancellationToken);

        public Task<string> GetPackageVersionsUrlAsync(string id, CancellationToken cancellationToken = default)
            => GetUrlAsync(
                g => g.GetPackageVersionsUrl(id),
                cancellationToken);

        public Task<string> GetPackageDownloadUrlAsync(string id, NuGetVersion version, CancellationToken cancellationToken = default)
            => GetUrlAsync(
                g => g.GetPackageDownloadUrl(id, version),
                cancellationToken);

        public Task<string> GetPackageManifestDownloadUrlAsync(string id, NuGetVersion version, CancellationToken cancellationToken = default)
            => GetUrlAsync(
                g => g.GetPackageManifestDownloadUrl(id, version),
                cancellationToken);

        private async Task<string> GetUrlAsync(Func<IUrlGenerator, string> urlAction, CancellationToken cancellationToken)
        {
            // TODO: This should periodically refresh the service index response.
            if (_urlGenerator == null)
            {
                await _mutex.WaitAsync(cancellationToken);

                try
                {
                    if (_urlGenerator == null)
                    {
                        var response = await _serviceIndex.GetAsync(cancellationToken);

                        _urlGenerator = new UrlGeneratorClient(response);
                    }
                }
                finally
                {
                    _mutex.Release();
                }
            }

            return urlAction(_urlGenerator);
        }
    }
}
