using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

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
        [JsonPropertyName("@id")]
        public string CatalogLeafUrl { get; set; }

        /// <summary>
        /// The ID of the package.
        /// </summary>
        [JsonPropertyName("id")]
        public string PackageId { get; set; }

        /// <summary>
        /// The full NuGet version after normalization, including any SemVer 2.0.0 build metadata.
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; }

        /// <summary>
        /// The package's authors.
        /// </summary>
        [JsonPropertyName("authors")]
        public string Authors { get; set; }

        /// <summary>
        /// The dependencies of the package, grouped by target framework.
        /// </summary>
        [JsonPropertyName("dependencyGroups")]
        public IReadOnlyList<DependencyGroupItem> DependencyGroups { get; set; }

        /// <summary>
        /// The deprecation associated with the package, if any.
        /// </summary>
        [JsonPropertyName("deprecation")]
        public PackageDeprecation Deprecation { get; set; }

        /// <summary>
        /// The package's description.
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; }

        /// <summary>
        /// The URL to the package's icon.
        /// </summary>
        [JsonPropertyName("iconUrl")]
        public string IconUrl { get; set; }

        /// <summary>
        /// The package's language.
        /// </summary>
        [JsonPropertyName("language")]
        public string Language { get; set; }

        /// <summary>
        /// The URL to the package's license.
        /// </summary>
        [JsonPropertyName("licenseUrl")]
        public string LicenseUrl { get; set; }

        /// <summary>
        /// Whether the package is listed in search results.
        /// If <see langword="null"/>, the package should be considered as listed.
        /// </summary>
        [JsonPropertyName("listed")]
        public bool? Listed { get; set; }

        /// <summary>
        /// The minimum NuGet client version needed to use this package.
        /// </summary>
        [JsonPropertyName("minClientVersion")]
        public string MinClientVersion { get; set; }

        /// <summary>
        /// The URL to download the package's content.
        /// </summary>
        [JsonPropertyName("packageContent")]
        public string PackageContentUrl { get; set; }

        /// <summary>
        /// The URL for the package's home page.
        /// </summary>
        [JsonPropertyName("projectUrl")]
        public string ProjectUrl { get; set; }

        /// <summary>
        /// The package's publish date.
        /// </summary>
        [JsonPropertyName("published")]
        public DateTimeOffset Published { get; set; }

        /// <summary>
        /// If true, the package requires its license to be accepted.
        /// </summary>
        [JsonPropertyName("requireLicenseAcceptance")]
        public bool RequireLicenseAcceptance { get; set; }

        /// <summary>
        /// The package's summary.
        /// </summary>
        [JsonPropertyName("summary")]
        public string Summary { get; set; }

        /// <summary>
        /// The package's tags.
        /// </summary>
        [JsonPropertyName("tags")]
        public IReadOnlyList<string> Tags { get; set; }

        /// <summary>
        /// The package's title.
        /// </summary>
        [JsonPropertyName("title")]
        public string Title { get; set; }
    }
}
