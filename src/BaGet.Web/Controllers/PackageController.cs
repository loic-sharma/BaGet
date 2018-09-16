using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Mirror;
using BaGet.Core.Services;
using Microsoft.AspNetCore.Mvc;
using NuGet.Packaging.Core;
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

        public async Task<IActionResult> Versions(string id)
        {
            var packages = await _packages.FindAsync(id);

            if (!packages.Any())
            {
                return NotFound();
            }

            return Json(new
            {
                Versions = packages.Select(p => p.VersionString).ToList()
            });
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

            var identity = new PackageIdentity(id, nugetVersion);
            var packageStream = await _storage.GetPackageStreamAsync(identity);

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

            var identity = new PackageIdentity(id, nugetVersion);

            return File(await _storage.GetNuspecStreamAsync(identity), "text/xml");
        }

        public async Task<IActionResult> DownloadReadme(string id, string version, CancellationToken cancellationToken)
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

            var identity = new PackageIdentity(id, nugetVersion);

            return File(await _storage.GetReadmeStreamAsync(identity), "text/markdown");
        }
    }
}