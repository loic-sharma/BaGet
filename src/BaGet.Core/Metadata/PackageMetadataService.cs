using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace BaGet.Core
{
    /// <summary>
    /// The Package Metadata client, used to fetch packages' metadata.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
    /// </summary>
    public class PackageMetadataService
    {
        private readonly IMirrorService _mirror;
        private readonly IPackageService _packages;
        private readonly RegistrationBuilder _builder;

        public PackageMetadataService(
            IMirrorService mirror,
            IPackageService packages,
            RegistrationBuilder builder)
        {
            _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
            _packages = packages ?? throw new ArgumentNullException(nameof(packages));
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        }

        /// <summary>
        /// Attempt to get a package's registration index, if it exists.
        /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#registration-page
        /// </summary>
        /// <param name="packageId">The package's ID.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The package's registration index, or null if the package does not exist</returns>
        public virtual async Task<BaGetRegistrationIndexResponse> GetRegistrationIndexOrNullAsync(
            string packageId,
            CancellationToken cancellationToken = default)
        {
            var packages = await FindPackagesOrNullAsync(packageId, cancellationToken);
            if (packages == null)
            {
                return null;
            }

            return _builder.BuildIndex(
                new PackageRegistration(
                    packageId,
                    packages));
        }

        /// <summary>
        /// Get the metadata for a single package version, if the package exists.
        /// </summary>
        /// <param name="packageId">The package's id.</param>
        /// <param name="packageVersion">The package's version.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The registration leaf, or null if the package does not exist.</returns>
        public virtual async Task<BaGetRegistrationLeafResponse> GetRegistrationLeafOrNullAsync(
            string packageId,
            NuGetVersion packageVersion,
            CancellationToken cancellationToken = default)
        {
            // Allow read-through caching to happen if it is configured.
            await _mirror.MirrorAsync(packageId, packageVersion, cancellationToken);

            var package = await _packages.FindOrNullAsync(packageId, packageVersion, includeUnlisted: true, cancellationToken);
            if (package == null)
            {
                return null;
            }

            return _builder.BuildLeaf(package);
        }

        private async Task<IReadOnlyList<Package>> FindPackagesOrNullAsync(
            string packageId,
            CancellationToken cancellationToken)
        {
            var upstreamPackages = await _mirror.FindPackagesOrNullAsync(packageId, cancellationToken);
            var localPackages = await _packages.FindAsync(packageId, includeUnlisted: true, cancellationToken);

            if (upstreamPackages == null)
            {
                return localPackages.Any()
                    ? localPackages
                    : null;
            }

            // Mrge the local packages into the upstream packages.
            var result = upstreamPackages.ToDictionary(p => new PackageIdentity(p.Id, p.Version));
            var local = localPackages.ToDictionary(p => new PackageIdentity(p.Id, p.Version));

            foreach (var localPackage in local)
            {
                result[localPackage.Key] = localPackage.Value;
            }

            return result.Values.ToList();
        }
    }
}
