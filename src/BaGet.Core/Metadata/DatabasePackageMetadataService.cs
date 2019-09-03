using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using BaGet.Core.Mirror;
using BaGet.Protocol;
using NuGet.Versioning;

namespace BaGet.Core.Metadata
{
    /// <inheritdoc />
    public class DatabasePackageMetadataService : IBaGetPackageMetadataService
    {
        private readonly IMirrorService _mirror;
        private readonly IPackageService _packages;
        private readonly IUrlGenerator _url;

        public DatabasePackageMetadataService(IMirrorService mirror, IPackageService packages, IUrlGenerator url)
        {
            _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
            _packages = packages ?? throw new ArgumentNullException(nameof(packages));
            _url = url ?? throw new ArgumentNullException(nameof(url));
        }

        public async Task<RegistrationIndexResponse> GetRegistrationIndexOrNullAsync(string id, CancellationToken cancellationToken = default)
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
                return null;
            }

            var versions = packages.Select(p => p.Version).ToList();

            // TODO: Paging of registration items.
            // "Un-paged" example: https://api.nuget.org/v3/registration3/newtonsoft.json/index.json
            // Paged example: https://api.nuget.org/v3/registration3/fake/index.json
            return new BaGetRegistrationIndexResponse(
                type: RegistrationIndexResponse.DefaultType,
                count: packages.Count,
                totalDownloads: packages.Sum(p => p.Downloads),
                pages: new[]
                {
                    new RegistrationIndexPage(
                        pageUrl: _url.GetRegistrationIndexUrl(packages.First().Id),
                        count: packages.Count(),
                        itemsOrNull: packages.Select(ToRegistrationIndexPageItem).ToList(),
                        lower: versions.Min(),
                        upper: versions.Max())
                });
        }

        public async Task<RegistrationLeafResponse> GetRegistrationLeafOrNullAsync(
            string id,
            NuGetVersion version,
            CancellationToken cancellationToken = default)
        {
            // Allow read-through caching to happen if it is configured.
            await _mirror.MirrorAsync(id, version, cancellationToken);

            var package = await _packages.FindOrNullAsync(id, version, includeUnlisted: true, cancellationToken);
            if (package == null)
            {
                return null;
            }

            return new BaGetRegistrationLeafResponse(
                type: RegistrationLeafResponse.DefaultType,
                registrationUri: _url.GetRegistrationLeafUrl(id, version),
                listed: package.Listed,
                downloads: package.Downloads,
                packageContentUrl: _url.GetPackageDownloadUrl(id, version),
                published: package.Published,
                registrationIndexUrl: _url.GetRegistrationIndexUrl(id));
        }

        public Task<RegistrationPageResponse> GetRegistrationPageOrNullAsync(
            string id,
            NuGetVersion lower,
            NuGetVersion upper,
            CancellationToken cancellationToken = default)
        {
            // TODO: BaGet does not support paging of registration items.
            // Currently, all items are inlined into the registration index.
            // Implementing this feature efficiently requires the ability to
            // sort packages by their versions from the database.
            throw new NotImplementedException();
        }

        private RegistrationIndexPageItem ToRegistrationIndexPageItem(Package package) =>
            new RegistrationIndexPageItem(
                leafUrl: _url.GetRegistrationLeafUrl(package.Id, package.Version),
                packageMetadata: new BaGetPackageMetadata(
                    catalogUri: _url.GetRegistrationLeafUrl(package.Id, package.Version),
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
                    packageContent: _url.GetPackageDownloadUrl(package.Id, package.Version),
                    packageTypes: package.PackageTypes.Select(t => t.Name).ToList(),
                    projectUrl: package.ProjectUrlString,
                    repositoryUrl: package.RepositoryUrlString,
                    repositoryType: package.RepositoryType,
                    published: package.Published,
                    requireLicenseAcceptance: package.RequireLicenseAcceptance,
                    summary: package.Summary,
                    tags: package.Tags,
                    title: package.Title,
                    dependencyGroups: ToDependencyGroups(package)),
                packageContent: _url.GetPackageDownloadUrl(package.Id, package.Version));

        private IReadOnlyList<DependencyGroupItem> ToDependencyGroups(Package package)
        {
            var groups = new List<DependencyGroupItem>();

            var targetFrameworks = package.Dependencies.Select(d => d.TargetFramework).Distinct();

            foreach (var targetFramework in targetFrameworks)
            {
                // A package that supports a target framework but does not have dependencies while on
                // that target framework is represented by a fake dependency with a null "Id" and "VersionRange".
                // This fake dependency should not be included in the output.
                groups.Add(new DependencyGroupItem
                {
                    TargetFramework = targetFramework,
                    Dependencies = package.Dependencies
                        .Where(d => d.TargetFramework == targetFramework)
                        .Where(d => d.Id != null && d.VersionRange != null)
                        .Select(d => new DependencyItem
                        {
                            Id = d.Id,
                            Range = d.VersionRange
                        })
                        .ToList(),
                });
            }

            return groups;
        }
    }
}
