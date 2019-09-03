using System;
using System.Collections.Generic;
using BaGet.Protocol.Internal;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    /// <summary>
    /// The metadata for a package.
    /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#catalog-entry
    /// </summary>
    public class PackageMetadata
    {
        public PackageMetadata(
            string catalogUri,
            string packageId,
            NuGetVersion version,
            string authors,
            string description,
            string iconUrl,
            string language,
            string licenseUrl,
            bool listed,
            string minClientVersion,
            string packageContent,
            string projectUrl,
            DateTime published,
            bool requireLicenseAcceptance,
            string summary,
            IReadOnlyList<string> tags,
            string title,
            IReadOnlyList<PackageDependencyGroup> dependencyGroups)
        {
            CatalogUri = catalogUri ?? throw new ArgumentNullException(nameof(catalogUri));

            PackageId = packageId;
            Version = version;
            Authors = authors;
            Description = description;
            IconUrl = iconUrl;
            Language = language;
            LicenseUrl = licenseUrl;
            Listed = listed;
            MinClientVersion = minClientVersion;
            PackageContent = packageContent;
            ProjectUrl = projectUrl;
            Published = published;
            RequireLicenseAcceptance = requireLicenseAcceptance;
            Summary = summary;
            Tags = tags;
            Title = title;
            DependencyGroups = dependencyGroups;
        }

        [JsonProperty(PropertyName = "@id")]
        public string CatalogUri { get; }

        [JsonProperty(PropertyName = "id")]
        public string PackageId { get; }

        /// <summary>
        /// The full NuGet version after normalization, including any semver2 build metadata.
        /// </summary>
        [JsonConverter(typeof(NuGetVersionConverter), NuGetVersionConversionFlags.IncludeBuildMetadata)]
        public NuGetVersion Version { get; }

        public string Authors { get; }
        public string Description { get; }
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
        public IReadOnlyList<string> Tags { get; }
        public string Title { get; }
        public IReadOnlyList<PackageDependencyGroup> DependencyGroups { get; }
    }
}
