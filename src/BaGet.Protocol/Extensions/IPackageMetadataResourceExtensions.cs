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
                // The package's metadata is either on the registration index's page,
                // or, it is be split into separate registration pages. If split,
                // "ItemsOrNull" will be null.
                var items = registrationIndexPage.ItemsOrNull;
                if (items == null)
                {
                    // "ItemsOrNull" is null if this registration index has external pages.
                    var externalRegistrationPage = await packageMetadata.GetRegistrationPageOrNullAsync(
                        packageId,
                        registrationIndexPage.Lower,
                        registrationIndexPage.Upper,
                        cancellationToken);

                    // TODO: Throw if this is null?
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

                // The package's metadata is either on the registration index's page,
                // or, it is be split into separate registration pages. If split,
                // "ItemsOrNull" will be null.
                var items = registrationIndexPage.ItemsOrNull;
                if (items == null)
                {
                    // "ItemsOrNull" is null if this registration index has external pages.
                    var externalRegistrationPage = await packageMetadata.GetRegistrationPageOrNullAsync(
                        packageId,
                        registrationIndexPage.Lower,
                        registrationIndexPage.Upper,
                        cancellationToken);

                    // TODO: Throw if this is null?
                    items = externalRegistrationPage.ItemsOrNull;
                }

                return items.SingleOrDefault(i => i.PackageMetadata.Version == packageVersion);
            }

            // No registration pages contained the desired version.
            return null;
        }
    }
}
