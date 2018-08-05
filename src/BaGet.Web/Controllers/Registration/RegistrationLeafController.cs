using System;
using System.Threading.Tasks;
using BaGet.Core.Mirror;
using BaGet.Core.Services;
using BaGet.Web.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace BaGet.Controllers.Web.Registration
{
    /// <summary>
    /// The API to retrieve the metadata of a specific version of a specific package.
    /// </summary>
    [Authorize]
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
        public async Task<IActionResult> Get(string id, string version)
        {
            if (!NuGetVersion.TryParse(version, out var nugetVersion))
            {
                return NotFound();
            }

            // Allow read-through caching to happen if it is confiured.
            await _mirror.MirrorAsync(id, nugetVersion);

            var package = await _packages.FindAsync(id, nugetVersion);

            if (package == null)
            {
                return NotFound();
            }

            // Documentation: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
            var result = new RegistrationLeaf(
                registrationUri: Url.PackageRegistration(id, nugetVersion),
                listed: package.Listed,
                downloads: package.Downloads,
                packageContentUri: Url.PackageDownload(id, nugetVersion),
                published: package.Published,
                registrationIndexUri: Url.PackageRegistration(id));

            return Json(result);
        }

        public class RegistrationLeaf
        {
            public RegistrationLeaf(
                string registrationUri,
                bool listed,
                long downloads,
                string packageContentUri,
                DateTimeOffset published,
                string registrationIndexUri)
            {
                RegistrationUri = registrationUri ?? throw new ArgumentNullException(nameof(registrationIndexUri));
                Listed = listed;
                Published = published;
                Downloads = downloads;
                PackageContent = packageContentUri ?? throw new ArgumentNullException(nameof(packageContentUri));
                RegistrationIndexUri = registrationIndexUri ?? throw new ArgumentNullException(nameof(registrationIndexUri));
            }

            [JsonProperty(PropertyName = "@id")]
            public string RegistrationUri { get; }

            public bool Listed { get; }

            public long Downloads { get; }

            public string PackageContent { get; }

            public DateTimeOffset Published { get; }

            [JsonProperty(PropertyName = "registration")]
            public string RegistrationIndexUri { get; }
        }
    }
}