using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;
using NuGet.Versioning;

namespace BaGet.Core
{
    /// <inheritdoc />
    public class DefaultPackageMetadataService : IPackageMetadataService
    {
        private readonly IPackageService _packages;
        private readonly RegistrationBuilder _builder;

        public DefaultPackageMetadataService(
            IPackageService packages,
            RegistrationBuilder builder)
        {
            _packages = packages ?? throw new ArgumentNullException(nameof(packages));
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        }

        public async Task<BaGetRegistrationIndexResponse> GetRegistrationIndexOrNullAsync(
            string packageId,
            CancellationToken cancellationToken = default)
        {
            var packages = await _packages.FindPackagesAsync(packageId, cancellationToken);
            if (!packages.Any())
            {
                return null;
            }

            return _builder.BuildIndex(
                new PackageRegistration(
                    packageId,
                    packages));
        }

        public async Task<RegistrationLeafResponse> GetRegistrationLeafOrNullAsync(
            string id,
            NuGetVersion version,
            CancellationToken cancellationToken = default)
        {
            var package = await _packages.FindPackageOrNullAsync(id, version, cancellationToken);
            if (package == null)
            {
                return null;
            }

            return _builder.BuildLeaf(package);
        }
    }
}
