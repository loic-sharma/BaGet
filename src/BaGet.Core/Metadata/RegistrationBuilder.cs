using System;
using System.Collections.Generic;
using System.Linq;
using BaGet.Protocol.Models;

namespace BaGet.Core
{
    public class RegistrationBuilder
    {
        private readonly IUrlGenerator _url;

        public RegistrationBuilder(IUrlGenerator url)
        {
            _url = url ?? throw new ArgumentNullException(nameof(url));
        }

        public virtual BaGetRegistrationIndexResponse BuildIndex(PackageRegistration registration)
        {
            var versions = registration.Packages.Select(p => p.Version).ToList();

            // TODO: Paging of registration items.
            // "Un-paged" example: https://api.nuget.org/v3/registration3/newtonsoft.json/index.json
            // Paged example: https://api.nuget.org/v3/registration3/fake/index.json
            return new BaGetRegistrationIndexResponse
            {
                RegistrationIndexUrl = _url.GetRegistrationIndexUrl(registration.PackageId),
                Type = RegistrationIndexResponse.DefaultType,
                Count = 1,
                TotalDownloads = registration.Packages.Sum(p => p.Downloads),
                Pages = new[]
                {
                    new RegistrationIndexPage
                    {
                        RegistrationPageUrl = _url.GetRegistrationIndexUrl(registration.PackageId),
                        Count = registration.Packages.Count(),
                        Lower = versions.Min().ToNormalizedString().ToLowerInvariant(),
                        Upper = versions.Max().ToNormalizedString().ToLowerInvariant(),
                        ItemsOrNull = registration.Packages.Select(ToRegistrationIndexPageItem).ToList(),
                    }
                }
            };
        }

        public virtual BaGetRegistrationLeafResponse BuildLeaf(Package package)
        {
            var id = package.Id;
            var version = package.Version;

            return new BaGetRegistrationLeafResponse
            {
                Type = RegistrationLeafResponse.DefaultType,
                Listed = package.Listed,
                Downloads = package.Downloads,
                Published = package.Published,
                RegistrationLeafUrl = _url.GetRegistrationLeafUrl(id, version),
                PackageContentUrl = _url.GetPackageDownloadUrl(id, version),
                RegistrationIndexUrl = _url.GetRegistrationIndexUrl(id)
            };
        }

        private RegistrationIndexPageItem ToRegistrationIndexPageItem(Package package) =>
            new RegistrationIndexPageItem
            {
                RegistrationLeafUrl = _url.GetRegistrationLeafUrl(package.Id, package.Version),
                PackageContentUrl = _url.GetPackageDownloadUrl(package.Id, package.Version),
                PackageMetadata = new BaGetPackageMetadata
                {
                    PackageId = package.Id,
                    Version = package.Version.ToFullString(),
                    Authors = string.Join(", ", package.Authors),
                    Description = package.Description,
                    Downloads = package.Downloads,
                    HasReadme = package.HasReadme,
                    IconUrl = package.IconUrlString,
                    Language = package.Language,
                    LicenseUrl = package.LicenseUrlString,
                    Listed = package.Listed,
                    MinClientVersion = package.MinClientVersion,
                    ReleaseNotes = package.ReleaseNotes,
                    PackageContentUrl = _url.GetPackageDownloadUrl(package.Id, package.Version),
                    PackageTypes = package.PackageTypes.Select(t => t.Name).ToList(),
                    ProjectUrl = package.ProjectUrlString,
                    RepositoryUrl = package.RepositoryUrlString,
                    RepositoryType = package.RepositoryType,
                    Published = package.Published,
                    RequireLicenseAcceptance = package.RequireLicenseAcceptance,
                    Summary = package.Summary,
                    Tags = package.Tags,
                    Title = package.Title,
                    DependencyGroups = ToDependencyGroups(package)
                },
            };

        private IReadOnlyList<DependencyGroupItem> ToDependencyGroups(Package package)
        {
            return package.Dependencies
                .GroupBy(d => d.TargetFramework)
                .Select(group => new DependencyGroupItem
                {
                    TargetFramework = group.Key,

                    // A package that supports a target framework but does not have dependencies while on
                    // that target framework is represented by a fake dependency with a null "Id" and "VersionRange".
                    // This fake dependency should not be included in the output.
                    Dependencies = group
                        .Where(d => d.Id != null && d.VersionRange != null)
                        .Select(d => new DependencyItem
                        {
                            Id = d.Id,
                            Range = d.VersionRange
                        })
                        .ToList()
                })
                .ToList();
        }
    }
}
