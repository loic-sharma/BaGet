using System;
using System.Collections.Generic;
using System.Linq;
using BaGet.Protocol.Models;
using NuGet.Frameworks;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    /// <summary>
    /// These are documented interpretations of values returned by the catalog API.
    /// Based off: https://github.com/NuGet/NuGet.Services.Metadata/blob/64af0b59c5a79e0143f0808b39946df9f16cb2e7/src/NuGet.Protocol.Catalog/Models/ModelExtensions.cs
    /// </summary>
    public static class CatalogModelExtensions
    {
        /// <summary>
        /// Gets the leaves that lie within the provided commit timestamp bounds. The result is sorted by commit
        /// timestamp, then package ID, then package version (SemVer order).
        /// </summary>
        /// <param name="catalogPage"></param>
        /// <param name="minCommitTimestamp">The exclusive lower time bound on <see cref="CatalogLeafItem.CommitTimestamp"/>.</param>
        /// <param name="maxCommitTimestamp">The inclusive upper time bound on <see cref="CatalogLeafItem.CommitTimestamp"/>.</param>
        /// <param name="excludeRedundantLeaves">Only show the latest leaf concerning each package.</param>
        public static List<CatalogLeafItem> GetLeavesInBounds(
            this CatalogPage catalogPage,
            DateTimeOffset minCommitTimestamp,
            DateTimeOffset maxCommitTimestamp,
            bool excludeRedundantLeaves)
        {
            var leaves = catalogPage
                .Items
                .Where(x => x.CommitTimestamp > minCommitTimestamp && x.CommitTimestamp <= maxCommitTimestamp)
                .OrderBy(x => x.CommitTimestamp);

            if (excludeRedundantLeaves)
            {
                leaves = leaves
                    .GroupBy(x => new
                    {
                        PackageId = x.PackageId.ToLowerInvariant(),
                        PackageVersion = x.ParsePackageVersion()
                    })
                    .Select(x => x.Last())
                    .OrderBy(x => x.CommitTimestamp);
            }

            return leaves
                .ThenBy(x => x.PackageId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ParsePackageVersion())
                .ToList();
        }

        /// <summary>
        /// Gets the pages that may have catalog leaves within the provided commit timestamp bounds. The result is
        /// sorted by commit timestamp.
        /// </summary>
        /// <param name="catalogIndex">The catalog index to fetch pages from.</param>
        /// <param name="minCommitTimestamp">The exclusive lower time bound on <see cref="CatalogPageItem.CommitTimestamp"/>.</param>
        /// <param name="maxCommitTimestamp">The inclusive upper time bound on <see cref="CatalogPageItem.CommitTimestamp"/>.</param>
        public static List<CatalogPageItem> GetPagesInBounds(
            this CatalogIndex catalogIndex,
            DateTimeOffset minCommitTimestamp,
            DateTimeOffset maxCommitTimestamp)
        {
            return catalogIndex
                .GetPagesInBoundsLazy(minCommitTimestamp, maxCommitTimestamp)
                .ToList();
        }

        private static IEnumerable<CatalogPageItem> GetPagesInBoundsLazy(
            this CatalogIndex catalogIndex,
            DateTimeOffset minCommitTimestamp,
            DateTimeOffset maxCommitTimestamp)
        {
            // Filter out pages that fall entirely before the minimum commit timestamp and sort the remaining pages by
            // commit timestamp.
            var upperRange = catalogIndex
                .Items
                .Where(x => x.CommitTimestamp > minCommitTimestamp)
                .OrderBy(x => x.CommitTimestamp);

            // Take pages from the sorted list until the commit timestamp goes past the maximum commit timestamp. This
            // essentially LINQ's TakeWhile plus one more element.
            foreach (var page in upperRange)
            {
                yield return page;

                if (page.CommitTimestamp > maxCommitTimestamp)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Parse the package version as a <see cref="NuGetVersion" />.
        /// </summary>
        /// <param name="leaf">The catalog leaf.</param>
        /// <returns>The package version.</returns>
        public static NuGetVersion ParsePackageVersion(this ICatalogLeafItem leaf)
        {
            return NuGetVersion.Parse(leaf.PackageVersion);
        }

        /// <summary>
        /// Parse the target framework as a <see cref="NuGetFramework" />.
        /// </summary>
        /// <param name="packageDependencyGroup">The package dependency group.</param>
        /// <returns>The framework.</returns>
        public static NuGetFramework ParseTargetFramework(this DependencyGroupItem packageDependencyGroup)
        {
            if (string.IsNullOrEmpty(packageDependencyGroup.TargetFramework))
            {
                return NuGetFramework.AnyFramework;
            }

            return NuGetFramework.Parse(packageDependencyGroup.TargetFramework);
        }

        /// <summary>
        /// Parse the version range as a <see cref="VersionRange"/>.
        /// </summary>
        /// <param name="packageDependency">The package dependency.</param>
        /// <returns>The version range.</returns>
        public static VersionRange ParseRange(this DependencyItem packageDependency)
        {
            // Server side treats invalid version ranges as empty strings.
            // Source: https://github.com/NuGet/NuGet.Services.Metadata/blob/382c214c60993edfd7158bc6d223fafeebbc920c/src/Catalog/Helpers/NuGetVersionUtility.cs#L25-L34
            // Client side treats empty string version ranges as the "all" range.
            // Source: https://github.com/NuGet/NuGet.Client/blob/849063018d8ee08625774a2dcd07ab84224dabb9/src/NuGet.Core/NuGet.Protocol/DependencyInfo/RegistrationUtility.cs#L20-L30
            // Example: https://api.nuget.org/v3/catalog0/data/2016.03.14.21.19.28/servicestack.extras.serilog.2.0.1.json
            if (!VersionRange.TryParse(packageDependency.Range, out var parsed))
            {
                return VersionRange.All;
            }

            return parsed;
        }

        /// <summary>
        /// Determines if the provided catalog leaf is a package delete.
        /// </summary>
        /// <param name="leaf">The catalog leaf.</param>
        /// <returns>True if the catalog leaf represents a package delete.</returns>
        public static bool IsPackageDelete(this CatalogLeafItem leaf)
        {
            return leaf.Type == "nuget:PackageDelete";
        }

        /// <summary>
        /// Determines if the provided catalog leaf is a package delete.
        /// </summary>
        /// <param name="leaf">The catalog leaf.</param>
        /// <returns>True if the catalog leaf represents a package delete.</returns>
        public static bool IsPackageDelete(this CatalogLeaf leaf)
        {
            return leaf.Type.FirstOrDefault() == "PackageDelete";
        }

        /// <summary>
        /// Determines if the provided catalog leaf is contains package details.
        /// </summary>
        /// <param name="leaf">The catalog leaf.</param>
        /// <returns>True if the catalog leaf contains package details.</returns>
        public static bool IsPackageDetails(this CatalogLeafItem leaf)
        {
            return leaf.Type == "nuget:PackageDetails";
        }

        /// <summary>
        /// Determines if the provided catalog leaf is contains package details.
        /// </summary>
        /// <param name="leaf">The catalog leaf.</param>
        /// <returns>True if the catalog leaf contains package details.</returns>
        public static bool IsPackageDetails(this CatalogLeaf leaf)
        {
            return leaf.Type.FirstOrDefault() == "PackageDetails";
        }

        /// <summary>
        /// Determines if the provided package details leaf represents a listed package.
        /// </summary>
        /// <param name="leaf">The catalog leaf.</param>
        /// <returns>True if the package is listed.</returns>
        public static bool IsListed(this PackageDetailsCatalogLeaf leaf)
        {
            if (leaf.Listed.HasValue)
            {
                return leaf.Listed.Value;
            }

            // A published year of 1900 indicates that this package is unlisted, when the listed property itself is
            // not present (legacy behavior).
            // Example: https://api.nuget.org/v3/catalog0/data/2015.02.01.06.22.45/antixss.4.0.1.json
            return leaf.Published.Year != 1900;
        }

        /// <summary>
        /// Determines if the provied package details leaf represents a SemVer 2.0.0 package. A package is considered
        /// SemVer 2.0.0 if it's version is SemVer 2.0.0 or one of its dependency version ranges is SemVer 2.0.0.
        /// </summary>
        /// <param name="leaf">The catalog leaf.</param>
        /// <returns>True if the package is SemVer 2.0.0.</returns>
        public static bool IsSemVer2(this PackageDetailsCatalogLeaf leaf)
        {
            var parsedPackageVersion = leaf.ParsePackageVersion();
            if (parsedPackageVersion.IsSemVer2)
            {
                return true;
            }

            if (leaf.VerbatimVersion != null)
            {
                var parsedVerbatimVersion = NuGetVersion.Parse(leaf.VerbatimVersion);
                if (parsedVerbatimVersion.IsSemVer2)
                {
                    return true;
                }
            }

            if (leaf.DependencyGroups != null)
            {
                foreach (var dependencyGroup in leaf.DependencyGroups)
                {
                    // Example: https://api.nuget.org/v3/catalog0/data/2018.10.28.07.42.42/mvcsitemapprovider.3.3.0-pre1.json
                    if (dependencyGroup.Dependencies == null)
                    {
                        continue;
                    }

                    foreach (var dependency in dependencyGroup.Dependencies)
                    {
                        var versionRange = dependency.ParseRange();
                        if ((versionRange.MaxVersion != null && versionRange.MaxVersion.IsSemVer2)
                            || (versionRange.MinVersion != null && versionRange.MinVersion.IsSemVer2))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
    }
}
