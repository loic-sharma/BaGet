using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BaGet.Protocol.Models;

namespace BaGet.Core;

public class RegistrationBuilder
{
    private readonly IUrlGenerator _url;

    public RegistrationBuilder(IUrlGenerator url)
    {
        _url = url ?? throw new ArgumentNullException(nameof(url));
    }

    public virtual BaGetRegistrationIndexResponse BuildIndex(PackageRegistration registration)
    {
        var sortedPackages = registration.Packages.OrderBy(p => p.Version).ToList();

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
                new BaGetRegistrationIndexPage
                {
                    RegistrationPageUrl = _url.GetRegistrationIndexUrl(registration.PackageId),
                    Count = registration.Packages.Count(),
                    Lower = sortedPackages.First().Version.ToNormalizedString().ToLowerInvariant(),
                    Upper = sortedPackages.Last().Version.ToNormalizedString().ToLowerInvariant(),
                    ItemsOrNull = sortedPackages.Select(ToRegistrationIndexPageItem).ToList(),
                }
            }
        };
    }

    public virtual RegistrationLeafResponse BuildLeaf(Package package)
    {
        var id = package.Id;
        var version = package.Version;

        return new RegistrationLeafResponse
        {
            Type = RegistrationLeafResponse.DefaultType,
            Listed = package.Listed,
            Published = package.Published,
            RegistrationLeafUrl = _url.GetRegistrationLeafUrl(id, version),
            PackageContentUrl = _url.GetPackageDownloadUrl(id, version),
            RegistrationIndexUrl = _url.GetRegistrationIndexUrl(id)
        };
    }

    #pragma warning disable CS8604
    private BaGetRegistrationIndexPageItem ToRegistrationIndexPageItem(Package package) =>
        new()
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
                IconUrl = package.HasEmbeddedIcon
                    ? _url.GetPackageIconDownloadUrl(package.Id, package.Version)
                    : package.IconUrlString,
                Language = package.Language,
                LicenseUrl = package.LicenseUrlString,
                Listed = package.Listed,
                MinClientVersion = package.MinClientVersion,
                ReleaseNotes = package.ReleaseNotes,
                PackageContentUrl = _url.GetPackageDownloadUrl(package.Id, package.Version),
                PackageTypes = package.PackageTypes?.Select(t => t.Name).ToList(),
                ProjectUrl = package.ProjectUrlString,
                RepositoryUrl = package.RepositoryUrlString,
                RepositoryType = package.RepositoryType,
                Published = package.Published==DateTime.MinValue?DateTimeOffset.Now : package.Published,
                RequireLicenseAcceptance = package.RequireLicenseAcceptance,
                Summary = package.Summary,
                Tags = package.Tags,
                Title = package.Title,
                DependencyGroups = ToDependencyGroups(package),
                Deprecation = ToDeprecation(package.Deprecation),
                Vulnerabilities = ToVulnerabilities(package.Vulnerabilities),
            },
        };
    #pragma warning restore CS8604

    // Inside BaGet.Core the entity BaGet.Core.PackageDeprecation is found before the
    // protocol model of the same name, so the protocol model is named in full.
    internal static Protocol.Models.PackageDeprecation? ToDeprecation(PackageDeprecation? deprecation)
    {
        if (deprecation == null)
        {
            return null;
        }

        return new Protocol.Models.PackageDeprecation
        {
            Reasons = deprecation.Reasons ?? Array.Empty<string>(),
            Message = deprecation.Message,
            AlternatePackage = string.IsNullOrEmpty(deprecation.AlternatePackageId)
                ? null
                : new AlternatePackage
                {
                    Id = deprecation.AlternatePackageId,

                    // The protocol writes "*" for "any version".
                    Range = string.IsNullOrEmpty(deprecation.AlternateVersionRange)
                        ? "*"
                        : deprecation.AlternateVersionRange,
                },
        };
    }

    internal static IReadOnlyList<PackageVulnerabilityMetadata>? ToVulnerabilities(
        IReadOnlyCollection<PackageVulnerability>? vulnerabilities)
    {
        // Null rather than empty, so a version with no known vulnerabilities serialises
        // exactly as it did before vulnerabilities existed.
        if (vulnerabilities == null || vulnerabilities.Count == 0)
        {
            return null;
        }

        return vulnerabilities
            .Select(v => new PackageVulnerabilityMetadata
            {
                AdvisoryUrl = v.AdvisoryUrl,
                Severity = v.Severity.ToString(CultureInfo.InvariantCulture),
            })
            .ToList();
    }

    internal static IReadOnlyList<DependencyGroupItem> ToDependencyGroups(Package package)
    {
        #pragma warning disable CS8604
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
        #pragma warning restore CS8604
    }
}