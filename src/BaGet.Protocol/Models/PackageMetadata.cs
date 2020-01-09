using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BaGet.Protocol.Models
{
    /// <summary>
    /// A package's metadata.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#catalog-entry
    /// </summary>
    public class PackageMetadata
    {
        /// <summary>
        /// The URL to the document used to produce this object.
        /// </summary>
        [JsonProperty("@id")]
        public string CatalogLeafUrl { get; set; }

        /// <summary>
        /// The ID of the package.
        /// </summary>
        [JsonProperty("id")]
        public string PackageId { get; set; }

        /// <summary>
        /// The full NuGet version after normalization, including any SemVer 2.0.0 build metadata.
        /// </summary>
        [JsonProperty("version")]
        public string Version { get; set; }

        /// <summary>
        /// The package's authors.
        /// </summary>
        [JsonProperty("authors")]
        public string Authors { get; set; }

        /// <summary>
        /// The package's description.
        /// </summary>
        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>
        /// The URL to the package's icon.
        /// </summary>
        [JsonProperty("iconUrl")]
        public string IconUrl { get; set; }

        /// <summary>
        /// The package's language.
        /// </summary>
        [JsonProperty("language")]
        public string Language { get; set; }

        /// <summary>
        /// The URL to the package's license.
        /// </summary>
        [JsonProperty("licenseUrl")]
        public string LicenseUrl { get; set; }

        /// <summary>
        /// Whether the package is listed in search results.
        /// If <see langword="null"/>, the package should be considered as listed.
        /// </summary>
        [JsonProperty("listed")]
        public bool? Listed { get; set; }

        /// <summary>
        /// The minimum NuGet client version needed to use this package.
        /// </summary>
        [JsonProperty("minClientVersion")]
        public string MinClientVersion { get; set; }

        /// <summary>
        /// The URL to download the package's content.
        /// </summary>
        [JsonProperty("packageContent")]
        public string PackageContentUrl { get; set; }

        /// <summary>
        /// The URL for the package's home page.
        /// </summary>
        [JsonProperty("projectUrl")]
        public string ProjectUrl { get; set; }

        /// <summary>
        /// The package's publish date.
        /// </summary>
        [JsonProperty("published")]
        public DateTimeOffset Published { get; set; }

        /// <summary>
        /// If true, the package requires its license to be accepted.
        /// </summary>
        [JsonProperty("requireLicenseAcceptance")]
        public bool RequireLicenseAcceptance { get; set; }

        /// <summary>
        /// The package's summary.
        /// </summary>
        [JsonProperty("summary")]
        public string Summary { get; set; }

        /// <summary>
        /// The package's tags.
        /// </summary>
        [JsonProperty("tags")]
        public IReadOnlyList<string> Tags { get; set; }

        /// <summary>
        /// The package's title.
        /// </summary>
        [JsonProperty("title")]
        public string Title { get; set; }

        /// <summary>
        /// The package's release notes.
        /// </summary>
        [JsonProperty("releaseNotes")]
        public string ReleaseNotes { get; set; }

        /// <summary>
        /// The dependencies of the package, grouped by target framework.
        /// </summary>
        [JsonProperty("dependencyGroups")]
        public IReadOnlyList<DependencyGroupItem> DependencyGroups { get; set; }
    }
}
