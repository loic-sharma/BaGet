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
        [JsonProperty(PropertyName = "@id")]
        public string CatalogUrl { get; set; }

        [JsonProperty(PropertyName = "id")]
        public string PackageId { get; set; }

        /// <summary>
        /// The full NuGet version after normalization, including any semver2 build metadata.
        /// </summary>
        [JsonConverter(typeof(NuGetVersionConverter), NuGetVersionConversionFlags.IncludeBuildMetadata)]
        public NuGetVersion Version { get; set; }

        public string Authors { get; set; }
        public string Description { get; set; }
        public string IconUrl { get; set; }
        public string Language { get; set; }
        public string LicenseUrl { get; set; }
        public bool Listed { get; set; }
        public string MinClientVersion { get; set; }

        [JsonProperty(PropertyName = "packageContent")]
        public string PackageContentUrl { get; set; }

        public string ProjectUrl { get; set; }
        public DateTime Published { get; set; }
        public bool RequireLicenseAcceptance { get; set; }
        public string Summary { get; set; }
        public IReadOnlyList<string> Tags { get; set; }
        public string Title { get; set; }
        public IReadOnlyList<DependencyGroupItem> DependencyGroups { get; set; }
    }
}
