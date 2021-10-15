using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Core
{
    /// <summary>
    /// The mirror service used when mirroring has been disabled.
    /// </summary>
    public class DisabledMirrorService : IMirrorService
    {
        private readonly IPackageService _packages;

        public DisabledMirrorService(IPackageService packages)
        {
            _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        }

        public async Task<Package> FindPackageOrNullAsync(
            string id,
            NuGetVersion version,
            CancellationToken cancellationToken)
        {
            return await _packages.FindOrNullAsync(id, version, includeUnlisted: true, cancellationToken);
        }

        public async Task<IReadOnlyList<NuGetVersion>> FindPackageVersionsAsync(string id, CancellationToken cancellationToken)
        {
            var packages = await _packages.FindAsync(id, includeUnlisted: true, cancellationToken);

            return packages.Select(p => p.Version).ToList();
        }

        public async Task<IReadOnlyList<Package>> FindPackagesAsync(string id, CancellationToken cancellationToken)
        {
            return await _packages.FindAsync(id, includeUnlisted: true, cancellationToken);
        }

        public Task MirrorAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
