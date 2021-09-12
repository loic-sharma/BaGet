using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Protocol.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using NuGet.Frameworks;
using NuGet.Versioning;

namespace BaGet.Web
{
    public class PackageModel : PageModel
    {
        private readonly IMirrorService _mirror;
        private readonly IUrlGenerator _url;

        public PackageModel(
            IMirrorService mirror,
            IUrlGenerator url)
        {
            _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
            _url = url ?? throw new ArgumentNullException(nameof(url));
        }

        public bool Found { get; private set; }

        public Package Package { get; private set; }

        public IReadOnlyList<DependencyGroupModel> DependencyGroups { get; private set; }
        public bool IsDotnetTool { get; private set; }
        public bool IsDotnetTemplate { get; private set; }
        public DateTime LastUpdated { get; private set; }
        public long TotalDownloads { get; private set; }
        public IReadOnlyList<VersionModel> Versions { get; private set; }

        public string IconUrl { get; private set; }
        public string LicenseUrl { get; private set; }
        public string PackageDownloadUrl { get; private set; }

        public async Task OnGetAsync(string id, string version, CancellationToken cancellationToken)
        {
            var packages = await _mirror.FindPackagesAsync(id, cancellationToken);
            var listedPackages = packages.Where(p => p.Listed).ToList();

            if (!listedPackages.Any())
            {
                Package = new Package { Id = id };
                Found = false;
                return;
            }

            // Try to find the requested version.
            if (NuGetVersion.TryParse(version, out var requestedVersion))
            {
                Package = listedPackages.SingleOrDefault(p => p.Version == requestedVersion);
            }

            // Otherwise display the latest version.
            if (Package == null)
            {
                Package = listedPackages.OrderByDescending(p => p.Version).First();
            }

            var packageVersion = Package.Version;

            Found = true;
            DependencyGroups = ToDependencyGroups(Package);
            IsDotnetTool = Package.PackageTypes.Any(t => t.Name.Equals("DotnetTool", StringComparison.OrdinalIgnoreCase));
            IsDotnetTemplate = Package.PackageTypes.Any(t => t.Name.Equals("Template", StringComparison.OrdinalIgnoreCase));
            LastUpdated = packages.Max(p => p.Published);
            TotalDownloads = packages.Sum(p => p.Downloads);
            Versions = ToVersions(listedPackages, packageVersion);

            IconUrl = Package.HasEmbeddedIcon
                ? _url.GetPackageIconDownloadUrl(Package.Id, packageVersion)
                : Package.IconUrlString;
            LicenseUrl = Package.LicenseUrlString;
            PackageDownloadUrl = _url.GetPackageDownloadUrl(Package.Id, packageVersion);
        }

        private IReadOnlyList<DependencyGroupModel> ToDependencyGroups(Package package)
        {
            return package
                .Dependencies
                .GroupBy(d => d.TargetFramework)
                .Select(group =>
                {
                    return new DependencyGroupModel
                    {
                        Name = PrettifyTargetFramework(group.Key),
                        Dependencies = group
                            .Select(d => new DependencyModel
                            {
                                PackageId = d.Id,
                                VersionSpec = (d.VersionRange != null)
                                    ? VersionRange.Parse(d.VersionRange).PrettyPrint()
                                    : string.Empty
                            })
                            .ToList()
                    };
                })
                .ToList();
        }

        private string PrettifyTargetFramework(string targetFramework)
        {
            if (targetFramework == null) return "All Frameworks";

            NuGetFramework framework;
            try
            {
                framework = NuGetFramework.Parse(targetFramework);
            }
            catch (Exception)
            {
                return targetFramework;
            }

            // Defer to NuGet client logic for .NET 5 and newer.
            if (framework.Version.Major >= 5 &&
                framework.Framework.Equals(FrameworkConstants.FrameworkIdentifiers.NetCoreApp,
                    StringComparison.OrdinalIgnoreCase))
            {
                return framework.ToString();
            }

            // Don't bother with portable class library
            if (framework.Framework.Equals(".NETPortable", StringComparison.OrdinalIgnoreCase))
            {
                return targetFramework;
            }

            var version = (framework.Version.Build == 0)
                ? framework.Version.ToString(2)
                : framework.Version.ToString(3);

            return $"{framework.Framework} {version}";
        }

        private IReadOnlyList<VersionModel> ToVersions(IReadOnlyList<Package> packages, NuGetVersion selectedVersion)
        {
            return packages
                .Select(p => new VersionModel
                {
                    Version = p.Version,
                    Downloads = p.Downloads,
                    Selected = p.Version == selectedVersion,
                    LastUpdated = p.Published,
                })
                .OrderByDescending(m => m.Version)
                .ToList();
        }

        public class DependencyGroupModel
        {
            public string Name { get; set; }
            public IReadOnlyList<DependencyModel> Dependencies { get; set; }
        }

        // TODO: Convert this to records.
        public class DependencyModel
        {
            public string PackageId { get; set; }
            public string VersionSpec { get; set; }
        }

        // TODO: Convert this to records.
        public class VersionModel
        {
            public NuGetVersion Version { get; set; }
            public long Downloads { get; set; }
            public bool Selected { get; set; }
            public DateTime LastUpdated { get; set; }
        }
    }
}
