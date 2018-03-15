using System;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace BaGet.Controllers
{
    public class PackageController : Controller
    {
        private readonly BaGetContext _context;
        private readonly IPackageStorageService _storage;

        public PackageController(BaGetContext context, IPackageStorageService storage)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public async Task<IActionResult> Versions(string id)
        {
            var versions = await _context.Packages
                .Where(p => p.Id == id)
                .Select(p => p.Version)
                .ToListAsync();

            if (!versions.Any())
            {
                return NotFound();
            }

            return Json(versions);
        }

        public async Task<IActionResult> DownloadPackage(string id, string version)
        {
            if (!NuGetVersion.TryParse(version, out var nugetVersion))
            {
                return NotFound();
            }

            var identity = new PackageIdentity(id, nugetVersion);

            return File(await _storage.GetPackageStreamAsync(identity), "application/octet-stream");
        }

        public async Task<IActionResult> DownloadNuspec(string id, string version)
        {
            if (!NuGetVersion.TryParse(version, out var nugetVersion))
            {
                return NotFound();
            }

            var identity = new PackageIdentity(id, nugetVersion);

            return File(await _storage.GetNuspecStreamAsync(identity), "text/xml");
        }
    }
}