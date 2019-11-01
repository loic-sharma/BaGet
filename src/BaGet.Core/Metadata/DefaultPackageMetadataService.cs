using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace BaGet.Core
{
    /// <inheritdoc />
    public class DefaultPackageMetadataService : IPackageMetadataService
    {
        private readonly IMirrorService _mirror;
        private readonly IPackageService _packages;
        private readonly RegistrationBuilder _builder;

        public DefaultPackageMetadataService(
            IMirrorService mirror,
            IPackageService packages,
            RegistrationBuilder builder)
        {
            _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
            _packages = packages ?? throw new ArgumentNullException(nameof(packages));
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        }

        public async Task<BaGetRegistrationIndexResponse> GetRegistrationIndexOrNullAsync(
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

        public async Task<BaGetRegistrationLeafResponse> GetRegistrationLeafOrNullAsync(
            string id,
            NuGetVersion version,
            CancellationToken cancellationToken = default)
        {
            // Allow read-through caching to happen if it is configured.
            await _mirror.MirrorAsync(id, version, cancellationToken);

            var package = await _packages.FindOrNullAsync(id, version, includeUnlisted: true, cancellationToken);
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
