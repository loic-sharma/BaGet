using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    public class PackageMetadataService : IPackageMetadataService
    {
        private readonly IServiceIndexService _serviceIndexService;
        private readonly IRegistrationClient _registrationClient;
        private readonly IPackageContentClient _packageContentClient;

        public PackageMetadataService(
            IServiceIndexService serviceIndexService,
            IRegistrationClient registrationClient,
            IPackageContentClient packageContentClient)
        {
            _serviceIndexService = serviceIndexService ?? throw new ArgumentNullException(nameof(serviceIndexService));
            _registrationClient = registrationClient ?? throw new ArgumentNullException(nameof(registrationClient));
            _packageContentClient = packageContentClient ?? throw new ArgumentNullException(nameof(packageContentClient));
        }

        public async Task<Uri> GetPackageContentUriAsync(string id, NuGetVersion version)
        {
            // TODO: This should first try to load the package content URL. If the service index
            // has no package content resource, this should fallback to the registration resource.
            var packageContentUrl = await _serviceIndexService.GetPackageContentUrlAsync();
            var packageId = id.ToLowerInvariant();
            var packageVersion = version.ToNormalizedString().ToLowerInvariant();

            return new Uri($"{packageContentUrl}/{packageId}/{packageVersion}/{packageId}.{packageVersion}.nupkg");
        }

        public async Task<IReadOnlyList<PackageMetadata>> GetAllMetadataOrNullAsync(
            string packageId,
            CancellationToken cancellationToken = default)
        {
            return await GetAllRegistrationEntriesOrNull(packageId, e => e, cancellationToken);
        }

        public async Task<IReadOnlyList<NuGetVersion>> GetAllVersionsOrNullAsync(
            string packageId,
            bool includeUnlisted = false,
            CancellationToken cancellationToken = default)
        {
            if (!includeUnlisted)
            {
                return await GetListedVersionsFromRegistrationResourceAsync(packageId, cancellationToken);
            }
            {
                return await GetAllVersionsFromPackageContentResourceAsync(packageId, cancellationToken);
            }
        }

        private async Task<IReadOnlyList<NuGetVersion>> GetListedVersionsFromRegistrationResourceAsync(
            string packageId,
            CancellationToken cancellationToken)
        {
            IEnumerable<NuGetVersion> GetVersionFromRegistrationEntry(IEnumerable<PackageMetadata> entries)
            {
                return entries.Where(e => e.Listed).Select(e => e.Version);
            }

            return await GetAllRegistrationEntriesOrNull(packageId, GetVersionFromRegistrationEntry, cancellationToken);
        }

        private async Task<IReadOnlyList<NuGetVersion>> GetAllVersionsFromPackageContentResourceAsync(
            string packageId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var packageContentUrl = await _serviceIndexService.GetPackageContentUrlAsync();
            var requestUrl = $"{packageContentUrl}/{packageId.ToLowerInvariant()}/index.json";

            var result = await _packageContentClient.GetPackageVersionsOrNullAsync(requestUrl);
            if (result == null)
            {
                return new NuGetVersion[0];
            }

            return result.Versions;
        }

        private async Task<IReadOnlyList<T>> GetAllRegistrationEntriesOrNull<T>(
            string packageId,
            Func<IEnumerable<PackageMetadata>, IEnumerable<T>> handler,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var registrationUrl = await _serviceIndexService.GetRegistrationUrlAsync();
            var requestUrl = $"{registrationUrl}/{packageId.ToLowerInvariant()}/index.json";

            var packageIndex = await _registrationClient.GetRegistrationIndexOrNullAsync(requestUrl);
            if (packageIndex == null)
            {
                return null;
            }

            var result = new List<T>();

            foreach (var page in packageIndex.Pages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // If the package's registration index is too big, its pages' items will be
                // stored at different URLs. We will need to fetch each page's items individually.
                // We can detect this case as the index's pages will have "null" items.
                var items = page.ItemsOrNull;

                if (items == null)
                {
                    var externalPage = await _registrationClient.GetRegistrationIndexPageAsync(page.PageUrl);

                    if (externalPage.ItemsOrNull == null)
                    {
                        // This should never happen...
                        continue;
                    }

                    items = externalPage.ItemsOrNull;
                }

                result.AddRange(handler(items.Select(i => i.PackageMetadata)));
            }

            return result;
        }
    }
}
