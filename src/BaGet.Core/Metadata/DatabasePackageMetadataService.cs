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
            return new BaGetRegistrationIndexResponse
            {
                RegistrationIndexUrl = _url.GetRegistrationIndexUrl(id),
                Type = RegistrationIndexResponse.DefaultType,
                Count = 1,
                TotalDownloads = packages.Sum(p => p.Downloads),
                Pages = new[]
                {
                    new RegistrationIndexPage
                    {
                        RegistrationPageUrl = _url.GetRegistrationIndexUrl(packages.First().Id),
                        Count = packages.Count(),
                        Lower = versions.Min().ToNormalizedString().ToLowerInvariant(),
                        Upper = versions.Max().ToNormalizedString().ToLowerInvariant(),
                        ItemsOrNull = packages.Select(ToRegistrationIndexPageItem).ToList(),
                    }
                }
            };
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
