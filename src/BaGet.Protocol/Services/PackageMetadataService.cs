using System;
using System.Collections.Generic;
using System.Linq;
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

        public async Task<IReadOnlyList<NuGetVersion>> GetAllVersionsAsync(string packageId, bool includeUnlisted = false)
        {
            if (!includeUnlisted)
            {
                return await GetAllListedVersionsFromRegistrationResourceAsync(packageId);
            }
            {
                return await GetAllVersionsFromPackageContentResourceAsync(packageId);
            }
        }

        private async Task<IReadOnlyList<NuGetVersion>> GetAllListedVersionsFromRegistrationResourceAsync(string packageId)
        {
            var registrationUrl = await _serviceIndexService.GetRegistrationUrlAsync();
            var requestUrl = $"{registrationUrl}/{packageId.ToLowerInvariant()}/index.json";

            var packageIndex = await _registrationClient.GetRegistrationIndexAsync(requestUrl);
            var result = new List<NuGetVersion>();

            foreach (var page in packageIndex.Pages)
            {
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

                result.AddRange(
                    items.Where(i => i.CatalogEntry.Listed).Select(i => i.CatalogEntry.Version));
            }

            return result;
        }

        private async Task<IReadOnlyList<NuGetVersion>> GetAllVersionsFromPackageContentResourceAsync(string packageId)
        {
            var packageContentUrl = await _serviceIndexService.GetPackageContentUrlAsync();
            var requestUrl = $"{packageContentUrl}/{packageId.ToLowerInvariant()}/index.json";

            var result = await _packageContentClient.GetPackageVersionsOrNullAsync(requestUrl);
            if (result == null)
            {
                return new NuGetVersion[0];
            }

            return result.Versions;
        }
    }
}
