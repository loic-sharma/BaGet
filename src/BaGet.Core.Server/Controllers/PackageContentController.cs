using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Content;
using BaGet.Protocol.Models;
using Microsoft.AspNetCore.Mvc;
using NuGet.Versioning;

namespace BaGet.Controllers
{
    /// <summary>
    /// The Package Content resource, used to download content from packages.
    /// See: https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource
    /// </summary>
    public class PackageContentController : Controller
    {
        private readonly IPackageContentService _content;

        public PackageContentController(IPackageContentService content)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
        }

        public async Task<ActionResult<PackageVersionsResponse>> GetPackageVersionsAsync(string id, CancellationToken cancellationToken)
        {
            var versions = await _content.GetPackageVersionsOrNullAsync(id, cancellationToken);
            if (versions == null)
            {
                return NotFound();
            }

            return versions;
        }

        public async Task<IActionResult> DownloadPackageAsync(string id, string version, CancellationToken cancellationToken)
        {
            if (!NuGetVersion.TryParse(version, out var nugetVersion))
            {
                return NotFound();
            }

            var packageStream = await _content.GetPackageContentStreamOrNullAsync(id, nugetVersion, cancellationToken);
            if (packageStream == null)
            {
                return NotFound();
            }

            return File(packageStream, "application/octet-stream");
        }

        public async Task<IActionResult> DownloadNuspecAsync(string id, string version, CancellationToken cancellationToken)
        {
            if (!NuGetVersion.TryParse(version, out var nugetVersion))
            {
                return NotFound();
            }

            var nuspecStream = await _content.GetPackageManifestStreamOrNullAsync(id, nugetVersion, cancellationToken);
            if (nuspecStream == null)
            {
                return NotFound();
            }

            return File(nuspecStream, "text/xml");
        }

        public async Task<IActionResult> DownloadReadmeAsync(string id, string version, CancellationToken cancellationToken)
        {
            if (!NuGetVersion.TryParse(version, out var nugetVersion))
            {
                return NotFound();
            }

            var readmeStream = await _content.GetPackageReadmeStreamOrNullAsync(id, nugetVersion, cancellationToken);
            if (readmeStream == null)
            {
                return NotFound();
            }

            return File(readmeStream, "text/markdown");
        }
    }
}
