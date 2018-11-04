using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using BaGet.Core.Services;
using BaGet.Extensions;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

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
                TotalDownloads = packages.Sum(p => p.Downloads),
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
                Authors = string.Join(", ", package.Authors);
                Description = package.Description;
                Downloads = package.Downloads;
                HasReadme = package.HasReadme;
                IconUrl = package.IconUrlString;
                Language = package.Language;
                LicenseUrl = package.LicenseUrlString;
                Listed = package.Listed;
                MinClientVersion = package.MinClientVersion;
                PackageContent = packageContent;
                ProjectUrl = package.ProjectUrlString;
                RepositoryUrl = package.RepositoryUrlString;
                RepositoryType = package.RepositoryType;
                Published = package.Published;
                RequireLicenseAcceptance = package.RequireLicenseAcceptance;
                Summary = package.Summary;
                Tags = package.Tags;
                Title = package.Title;
                DependencyGroups = ToDependencyItems(package);
            }

            private IReadOnlyList<DependencyGroupItem> ToDependencyItems(Package package)
            {
                var groups = new List<DependencyGroupItem>();

                var targetFrameworks = package.Dependencies.Select(d => d.TargetFramework).Distinct();

                foreach(var target in targetFrameworks)
                {
                    // A package may have no dependencies for a target framework. This is represented
                    // by a single dependency item with a null "Id" and "VersionRange".
                    var groupId = $"https://api.nuget.org/v3/catalog0/data/2015.02.01.06.24.15/{package.Id}.{package.Version}.json#dependencygroup/{target}";
                    var dependencyItems = package.Dependencies
                        .Where(d => d.TargetFramework == target)
                        .Where(d => d.Id != null && d.VersionRange != null)
                        .Select(d => new DependencyItem(groupId, d.Id, d.VersionRange))
                        .ToList();

                    groups.Add(new DependencyGroupItem(groupId, target, dependencyItems));
                }

                return groups;
            }

            [JsonProperty(PropertyName = "@id")]
            public string CatalogUri { get; }

            [JsonProperty(PropertyName = "id")]
            public string PackageId { get; }

            public string Version { get; }
            public string Authors { get; }
            public string Description { get; }
            public long Downloads { get; }
            public bool HasReadme { get; }
            public string IconUrl { get; }
            public string Language { get; }
            public string LicenseUrl { get; }
            public bool Listed { get; }
            public string MinClientVersion { get; }
            public string PackageContent { get; }
            public string ProjectUrl { get; }
            public string RepositoryUrl { get; }
            public string RepositoryType { get; }
            public DateTime Published { get; }
            public bool RequireLicenseAcceptance { get; }
            public string Summary { get; }
            public string[] Tags { get; }
            public string Title { get; }
            public IReadOnlyList<DependencyGroupItem> DependencyGroups { get; }
        }

        private class DependencyGroupItem
        {
            public DependencyGroupItem(
                string id,
                string targetFramework,
                IReadOnlyList<DependencyItem> dependencyItems)
            {
                Id = id;
                Type = "PackageDependencyGroup";
                TargetFramework = targetFramework;
                Dependencies = (dependencyItems.Count > 0) ? dependencyItems : null;
            }

            [JsonProperty(PropertyName = "@id")]
            public string Id { get; }

            [JsonProperty(PropertyName = "@type")]
            public string Type { get; }

            public string TargetFramework { get;  }

            [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)]
            public IReadOnlyList<DependencyItem> Dependencies { get; }
        }

        private class DependencyItem
        {
            [JsonProperty(PropertyName = "@id")]
            public string DepId { get; }

            [JsonProperty(PropertyName = "@type")]
            public string Type { get; }

            public string Id { get; }
            public string Range { get; }

            public DependencyItem(string groupId, string depId, string versionRange)
            {
                DepId = groupId + "/" + depId;
                Type = "PackageDependency";
                Id = depId;
                Range = versionRange;
            }
        }
    }
}