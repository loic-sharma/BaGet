using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    public static class IPackageMetadataResourceExtensions
    {
        /// <summary>
        /// Fetch the metadata for all versions of a package.
        /// </summary>
        /// <param name="packageMetadata">The client to interact with the NuGet Package Metadata resource.</param>
        /// <param name="packageId">The desired package ID.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The metadata for a package, or null if the package could not be found.</returns>
        public static async Task<IReadOnlyList<RegistrationIndexPageItem>> GetRegistrationItemsOrNullAsync(
            this IPackageMetadataResource packageMetadata,
            string packageId,
            CancellationToken cancellationToken = default)
        {
            var registrationIndex = await packageMetadata.GetRegistrationIndexOrNullAsync(packageId, cancellationToken);
            if (registrationIndex == null)
            {
                return null;
            }

            var result = new List<RegistrationIndexPageItem>();
            foreach (var registrationIndexPage in registrationIndex.Pages)
            {
                // If the package's registration index is too big, it will be split into registration
                // pages stored at different URLs. We will need to fetch each page's items individually.
                // We can detect this case as the registration index will have "null" items.
                var items = registrationIndexPage.ItemsOrNull;
                if (items == null)
                {
                    var externalRegistrationPage = await packageMetadata.GetRegistrationPageOrNullAsync(
                        packageId,
                        registrationIndexPage.Lower,
                        registrationIndexPage.Upper,
                        cancellationToken);

                    // Skip malformed external pages.
                    if (externalRegistrationPage?.ItemsOrNull == null) continue;

                    items = externalRegistrationPage.ItemsOrNull;
                }

                result.AddRange(items);
            }

            return result;
        }

        /// <summary>
        /// Fetch the metadata for a single version of a package.
        /// </summary>
        /// <param name="packageMetadata">The client to interact with the NuGet Package Metadata resource.</param>
        /// <param name="packageId">The desired package ID.</param>
        /// <param name="packageVersion">The desired package version.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The package's metadata, or null if the package could not be found.</returns>
        public static async Task<RegistrationIndexPageItem> GetRegistrationItemOrNullAsync(
            this IPackageMetadataResource packageMetadata,
            string packageId,
            NuGetVersion packageVersion,
            CancellationToken cancellationToken = default)
        {
            var registrationIndex = await packageMetadata.GetRegistrationIndexOrNullAsync(packageId, cancellationToken);
            if (registrationIndex == null)
            {
                return null;
            }

            var result = new List<RegistrationIndexPageItem>();
            foreach (var registrationIndexPage in registrationIndex.Pages)
            {
                // Skip pages that do not contain the desired package version.
                if (registrationIndexPage.Lower > packageVersion) continue;
                if (registrationIndexPage.Upper < packageVersion) continue;

                // If the package's registration index is too big, it will be split into registration
                // pages stored at different URLs. We will need to fetch each page's items individually.
                // We can detect this case as the registration index will have "null" items.
                var items = registrationIndexPage.ItemsOrNull;
                if (items == null)
                {
                    var externalRegistrationPage = await packageMetadata.GetRegistrationPageOrNullAsync(
                        packageId,
                        registrationIndexPage.Lower,
                        registrationIndexPage.Upper,
                        cancellationToken);

                    // Skip malformed external pages.
                    if (externalRegistrationPage?.ItemsOrNull == null) continue;

                    items = externalRegistrationPage.ItemsOrNull;
                }

                return items.SingleOrDefault(i => i.PackageMetadata.Version == packageVersion);
            }

            // No registration pages contained the desired version.
            return null;
        }
    }
}
