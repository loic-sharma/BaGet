using System;
using System.Collections.Generic;
using BaGet.Protocol;
using NuGet.Versioning;

namespace BaGet.Core.Metadata
{
    /// <summary>
    /// BaGet's extensions to the package metadata model. These additions
    /// are not part of the official protocol.
    /// </summary>
    public class BaGetPackageMetadata : PackageMetadata
    {
        public BaGetPackageMetadata(
            string catalogUri,
            string packageId,
            NuGetVersion version,
            string authors,
            string description,
            long downloads,
            bool hasReadme,
            string iconUrl,
            string language,
            string licenseUrl,
            bool listed,
            string minClientVersion,
            string packageContent,
            IReadOnlyList<string> packageTypes,
            string projectUrl,
            string repositoryUrl,
            string repositoryType,
            DateTime published,
            bool requireLicenseAcceptance,
            string summary,
            IReadOnlyList<string> tags,
            string title,
            IReadOnlyList<PackageDependencyGroup> dependencyGroups)
          : base(
                catalogUri,
                packageId,
                version,
                authors,
                description,
                iconUrl,
                language,
                licenseUrl,
                listed,
                minClientVersion,
                packageContent,
                projectUrl,
                published,
                requireLicenseAcceptance,
                summary,
                tags,
                title,
                dependencyGroups)
        {
            Downloads = downloads;
            HasReadme = hasReadme;
            PackageTypes = packageTypes;
            RepositoryUrl = repositoryUrl;
            RepositoryType = repositoryType;
        }

        public long Downloads { get; }
        public bool HasReadme { get; }
        public IReadOnlyList<string> PackageTypes { get; }
        public string RepositoryUrl { get; }
        public string RepositoryType { get; }
    }
}
