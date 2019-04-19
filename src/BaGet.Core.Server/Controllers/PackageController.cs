using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Mirror;
using BaGet.Core.State;
using BaGet.Core.Storage;
using BaGet.Protocol;
using Microsoft.AspNetCore.Mvc;
using NuGet.Versioning;

namespace BaGet.Controllers
{
    public class PackageController : Controller
    {
        private readonly IMirrorService _mirror;
        private readonly IPackageService _packages;
        private readonly IPackageStorageService _storage;

        public PackageController(IMirrorService mirror, IPackageService packages, IPackageStorageService storage)
        {
            _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
            _packages = packages ?? throw new ArgumentNullException(nameof(packages));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public async Task<IActionResult> Versions(string id, CancellationToken cancellationToken)
        {
            // First, attempt to find all package versions using the upstream source.
            var versions = await _mirror.FindPackageVersionsOrNullAsync(id, cancellationToken);

            if (versions == null)
            {
                // Fallback to the local packages if the package couldn't be found
                // on the upstream source.
                var packages = await _packages.FindAsync(id);

                if (!packages.Any())
                {
                    return NotFound();
                }

                versions = packages.Select(p => p.Version).ToList();
            }

            return Json(new PackageVersions(versions));
        }

        public async Task<IActionResult> DownloadPackage(string id, string version, CancellationToken cancellationToken)
        {
            if (!NuGetVersion.TryParse(version, out var nugetVersion))
            {
                return NotFound();
            }

            // Allow read-through caching if it is configured.
            await _mirror.MirrorAsync(id, nugetVersion, cancellationToken);

            if (!await _packages.AddDownloadAsync(id, nugetVersion))
            {
                return NotFound();
            }

            var packageStream = await _storage.GetPackageStreamAsync(id, nugetVersion, cancellationToken);

            return File(packageStream, "application/octet-stream");
        }

        public async Task<IActionResult> DownloadNuspec(string id, string version, CancellationToken cancellationToken)
        {
            if (!NuGetVersion.TryParse(version, out var nugetVersion))
            {
                return NotFound();
            }

            // Allow read-through caching if it is configured.
            await _mirror.MirrorAsync(id, nugetVersion, cancellationToken);

            if (!await _packages.ExistsAsync(id, nugetVersion))
            {
                return NotFound();
            }

            var nuspecStream = await _storage.GetNuspecStreamAsync(id, nugetVersion, cancellationToken);

            return File(nuspecStream, "text/xml");
        }

        public async Task<IActionResult> DownloadReadme(string id, string version, CancellationToken cancellationToken)
        {
            if (!NuGetVersion.TryParse(version, out var nugetVersion))
            {
                return NotFound();
            }

            // Allow read-through caching if it is configured.
            await _mirror.MirrorAsync(id, nugetVersion, cancellationToken);

            var package = await _packages.FindOrNullAsync(id, nugetVersion);

            if (package == null || !package.HasReadme)
            {
                return NotFound();
            }

            var readmeStream = await _storage.GetReadmeStreamAsync(id, nugetVersion, cancellationToken);

            return File(readmeStream, "text/markdown");
        }
    }
}
