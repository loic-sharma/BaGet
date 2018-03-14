using System;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace BaGet.Controllers.Registration
{
    /// <summary>
    /// The API to retrieve the metadata of a specific version of a specific package.
    /// </summary>
    public class RegistrationLeafController : Controller
    {
        private readonly BaGetContext _context;

        public RegistrationLeafController(BaGetContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        // GET v3/registration/{id}/{version}.json
        [HttpGet]
        public async Task<IActionResult> Get(string id, string version)
        {
            if (!NuGetVersion.TryParse(version, out var nugetVersion))
            {
                return NotFound();
            }

            var package = await _context.Packages
                .Where(p => p.Id == id)
                .Where(p => p.Version == nugetVersion.ToNormalizedString())
                .FirstOrDefaultAsync();

            if (package == null)
            {
                return NotFound();
            }

            // Documentation: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
            var result = new RegistrationLeaf(
                registrationUri: Url.PackageRegistrationLeaf(id, nugetVersion),
                listed: package.Listed,
                packageContentUri: Url.PackageDownload(id, nugetVersion),
                published: package.Published,
                registrationIndexUri: Url.PackageRegistrationIndex(id));

            return Json(result);
        }

        public class RegistrationLeaf
        {
            public RegistrationLeaf(
                string registrationUri,
                bool listed,
                string packageContentUri,
                DateTimeOffset published,
                string registrationIndexUri)
            {
                RegistrationUri = registrationUri ?? throw new ArgumentNullException(nameof(registrationIndexUri));
                Listed = listed;
                Published = published;
                PackageContent = packageContentUri ?? throw new ArgumentNullException(nameof(packageContentUri));
                RegistrationIndexUri = registrationIndexUri ?? throw new ArgumentNullException(nameof(registrationIndexUri));
            }

            [JsonProperty(PropertyName = "@id")]
            public string RegistrationUri { get; }

            public bool Listed { get; }

            public string PackageContent { get; }

            public DateTimeOffset Published { get; }

            [JsonProperty(PropertyName = "registration")]
            public string RegistrationIndexUri { get; }
        }
    }
}