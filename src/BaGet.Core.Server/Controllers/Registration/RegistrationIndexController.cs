using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using BaGet.Core.Mirror;
using BaGet.Core.Services;
using BaGet.Extensions;
using BaGet.Protocol;
using Microsoft.AspNetCore.Mvc;

namespace BaGet.Controllers.Registration
{
    /// <summary>
    /// The API to retrieve the metadata of a specific package.
    /// </summary>
    public class RegistrationIndexController : Controller
    {
        private readonly IMirrorService _mirror;
        private readonly IPackageService _packages;

        public RegistrationIndexController(IMirrorService mirror, IPackageService packages)
        {
            _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
            _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        }

        // GET v3/registration/{id}.json
        [HttpGet]
        public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
        {
            // Find the packages that match the given package id from the upstream, if
            // one is configured. If these packages cannot be found on the upstream,
            // we'll return the local packages instead.
            var packages = await _mirror.FindPackagesOrNullAsync(id, cancellationToken);

            if (packages == null)
            {
                packages = await _packages.FindAsync(id, includeUnlisted: true);
            }

            if (!packages.Any())
            {
                return NotFound();
            }

            var versions = packages.Select(p => p.Version).ToList();

            // TODO: Paging of registration items.
            // "Un-paged" example: https://api.nuget.org/v3/registration3/newtonsoft.json/index.json
            // Paged example: https://api.nuget.org/v3/registration3/fake/index.json
            return Json(new RegistrationIndex(
                type: RegistrationIndex.DefaultType,
                count: packages.Count,
                totalDownloads: packages.Sum(p => p.Downloads),
                pages: new[]
                {
                    new RegistrationIndexPage(
                        Url.PackageRegistration(packages.First().Id),
                        count: packages.Count(),
                        itemsOrNull: packages.Select(ToRegistrationIndexPageItem).ToList(),
                        lower: versions.Min(),
                        upper: versions.Max())
                }));
        }

        private RegistrationIndexPageItem ToRegistrationIndexPageItem(Package package) =>
            new RegistrationIndexPageItem(
                leafUrl: Url.PackageRegistration(package.Id, package.Version),
                packageMetadata: new PackageMetadata(
                    catalogUri: Url.PackageRegistration(package.Id, package.Version),
                    packageId: package.Id,
                    version: package.Version,
                    authors: string.Join(", ", package.Authors),
                    description: package.Description,
                    downloads: package.Downloads,
                    hasReadme: package.HasReadme,
                    iconUrl: package.IconUrlString,
                    language: package.Language,
                    licenseUrl: package.LicenseUrlString,
                    listed: package.Listed,
                    minClientVersion: package.MinClientVersion,
                    packageContent: Url.PackageDownload(package.Id, package.Version),
                    projectUrl: package.ProjectUrlString,
                    repositoryUrl: package.RepositoryUrlString,
                    repositoryType: package.RepositoryType,
                    published: package.Published,
                    requireLicenseAcceptance: package.RequireLicenseAcceptance,
                    summary: package.Summary,
                    tags: package.Tags,
                    title: package.Title,
                    dependencyGroups: ToDependencyGroups(package)),
                packageContent: Url.PackageDownload(package.Id, package.Version));

        private IReadOnlyList<DependencyGroupItem> ToDependencyGroups(Package package)
        {
            var groups = new List<DependencyGroupItem>();

            var targetFrameworks = package.Dependencies.Select(d => d.TargetFramework).Distinct();

            foreach (var target in targetFrameworks)
            {
                // A package may have no dependencies for a target framework. This is represented
                // by a single dependency item with a null "Id" and "VersionRange".
                var groupId = $"https://api.nuget.org/v3/catalog0/data/2015.02.01.06.24.15/{package.Id}.{package.Version}.json#dependencygroup/{target}";
                var dependencyItems = package.Dependencies
                    .Where(d => d.TargetFramework == target)
                    .Where(d => d.Id != null && d.VersionRange != null)
                    .Select(d => new DependencyItem($"{groupId}/{d.Id}", d.Id, d.VersionRange))
                    .ToList();

                groups.Add(new DependencyGroupItem(groupId, target, dependencyItems));
            }

            return groups;
        }
    }
}
