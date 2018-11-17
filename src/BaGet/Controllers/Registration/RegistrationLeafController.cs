using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Mirror;
using BaGet.Core.Services;
using BaGet.Extensions;
using BaGet.Protocol;
using Microsoft.AspNetCore.Mvc;
using NuGet.Versioning;

namespace BaGet.Controllers.Registration
{
    /// <summary>
    /// The API to retrieve the metadata of a specific version of a specific package.
    /// </summary>
    public class RegistrationLeafController : Controller
    {
        private readonly IMirrorService _mirror;
        private readonly IPackageService _packages;

        public RegistrationLeafController(IMirrorService mirror, IPackageService packages)
        {
            _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
            _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        }

        // GET v3/registration/{id}/{version}.json
        [HttpGet]
        public async Task<IActionResult> Get(string id, string version, CancellationToken cancellationToken)
        {
            if (!NuGetVersion.TryParse(version, out var nugetVersion))
            {
                return NotFound();
            }

            // Allow read-through caching to happen if it is configured.
            await _mirror.MirrorAsync(id, nugetVersion, cancellationToken);

            var package = await _packages.FindOrNullAsync(id, nugetVersion, includeUnlisted: true);

            if (package == null)
            {
                return NotFound();
            }

            var result = new RegistrationLeaf(
                registrationUri: Url.PackageRegistration(id, nugetVersion),
                listed: package.Listed,
                downloads: package.Downloads,
                packageContentUrl: Url.PackageDownload(id, nugetVersion),
                published: package.Published,
                registrationIndexUrl: Url.PackageRegistration(id));

            return Json(result);
        }
    }
}