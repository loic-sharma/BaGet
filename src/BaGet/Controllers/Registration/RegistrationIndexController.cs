using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using BaGet.Core.Services;
using BaGet.Extensions;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace BaGet.Controllers.Registration
{
    /// <summary>
    /// The API to retrieve the metadata of a specific package.
    /// </summary>
    public class RegistrationIndexController : Controller
    {
        private readonly IPackageService _packages;

        public RegistrationIndexController(IPackageService packages)
        {
            _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        }

        // GET v3/registration/{id}.json
        [HttpGet]
        public async Task<IActionResult> Get(string id)
        {
            // Documentation: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
            var packages = await _packages.FindAsync(id);
            var versions = packages.Select(p => p.Version).ToList();

            if (!packages.Any())
            {
                return NotFound();
            }

            // TODO: Paging of registration items.
            // "Un-paged" example: https://api.nuget.org/v3/registration3/newtonsoft.json/index.json
            // Paged example: https://api.nuget.org/v3/registration3/fake/index.json
            return Json(new
            {
                Count = packages.Count,
                Items = new[]
                {
                    new RegistrationIndexItem(
                        packageId: id,
                        items: packages.Select(ToRegistrationIndexLeaf).ToList(),
                        lower: versions.Min().ToNormalizedString(),
                        upper: versions.Max().ToNormalizedString()
                    ),
                }
            });
        }

        private RegistrationIndexLeaf ToRegistrationIndexLeaf(Package package) =>
            new RegistrationIndexLeaf(
                packageId: package.Id,
                catalogEntry: new CatalogEntry(
                    package: package,
                    catalogUri: $"https://api.nuget.org/v3/catalog0/data/2015.02.01.06.24.15/{package.Id}.{package.Version}.json",
                    packageContent: Url.PackageDownload(package.Id, package.Version)),
                packageContent: Url.PackageDownload(package.Id, package.Version));

        private class RegistrationIndexItem
        {
            public RegistrationIndexItem(
                string packageId,
                IReadOnlyList<RegistrationIndexLeaf> items,
                string lower,
                string upper)
            {
                if (string.IsNullOrEmpty(packageId)) throw new ArgumentNullException(nameof(packageId));
                if (string.IsNullOrEmpty(lower)) throw new ArgumentNullException(nameof(lower));
                if (string.IsNullOrEmpty(upper)) throw new ArgumentNullException(nameof(upper));

                PackageId = packageId;
                Items = items ?? throw new ArgumentNullException(nameof(items));
                Lower = lower;
                Upper = upper;
            }

            [JsonProperty(PropertyName = "id")]
            public string PackageId { get; }

            public int Count => Items.Count;

            public IReadOnlyList<RegistrationIndexLeaf> Items { get; }

            public string Lower { get; }
            public string Upper { get; }
        }

        private class RegistrationIndexLeaf
        {
            public RegistrationIndexLeaf(string packageId, CatalogEntry catalogEntry, string packageContent)
            {
                if (string.IsNullOrEmpty(packageId)) throw new ArgumentNullException(nameof(packageId));

                PackageId = packageId;
                CatalogEntry = catalogEntry ?? throw new ArgumentNullException(nameof(catalogEntry));
                PackageContent = packageContent ?? throw new ArgumentNullException(nameof(packageContent));
            }

            [JsonProperty(PropertyName = "id")]
            public string PackageId { get; }

            public CatalogEntry CatalogEntry { get; }

            public string PackageContent { get; }
        }

        private class CatalogEntry
        {
            public CatalogEntry(Package package, string catalogUri, string packageContent)
            {
                if (package == null) throw new ArgumentNullException(nameof(package));

                CatalogUri = catalogUri ?? throw new ArgumentNullException(nameof(catalogUri));

                PackageId = package.Id;
                Version = package.VersionString;
                Authors = package.Authors;
                Description = package.Description;
                HasReadme = package.HasReadme;
                IconUrl = package.IconUrlString;
                Language = package.Language;
                LicenseUrl = package.LicenseUrlString;
                Listed = package.Listed;
                MinClientVersion = package.MinClientVersion;
                PackageContent = packageContent;
                ProjectUrl = package.ProjectUrlString;
                Published = package.Published;
                RequireLicenseAcceptance = package.RequireLicenseAcceptance;
                Summary = package.Summary;
                Tags = package.Tags;
                Title = package.Title;
            }

            [JsonProperty(PropertyName = "@id")]
            public string CatalogUri { get; }

            [JsonProperty(PropertyName = "id")]
            public string PackageId { get; }

            public string Version { get; }
            public string Authors { get; }
            public string Description { get; }
            public bool HasReadme { get; }
            public string IconUrl { get; }
            public string Language { get; }
            public string LicenseUrl { get; }
            public bool Listed { get; }
            public string MinClientVersion { get; }
            public string PackageContent { get; }
            public string ProjectUrl { get; }
            public DateTime Published { get; }
            public bool RequireLicenseAcceptance { get; }
            public string Summary { get; }
            public string[] Tags { get; }
            public string Title { get; }
        }
    }
}