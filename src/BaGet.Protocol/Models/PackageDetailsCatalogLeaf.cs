using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BaGet.Protocol.Models
{
    /// This class is based off https://github.com/NuGet/NuGet.Services.Metadata/blob/64af0b59c5a79e0143f0808b39946df9f16cb2e7/src/NuGet.Protocol.Catalog/Models/PackageDetailsCatalogLeaf.cs

    /// <summary>
    /// A "package details" catalog leaf. Represents a single package create or update event.
    /// <see cref="PackageDetailsCatalogLeaf"/>s can be discovered from a <see cref="CatalogPage"/>.
    /// 
    /// See: https://docs.microsoft.com/en-us/nuget/api/catalog-resource#catalog-leaf
    /// </summary>
    public class PackageDetailsCatalogLeaf : CatalogLeaf
    {
        /// <summary>
        /// The package's authors.
        /// </summary>
        [JsonPropertyName("authors")]
        public string Authors { get; set; }

        /// <summary>
        /// The package's copyright.
        /// </summary>
        [JsonPropertyName("copyright")]
        public string Copyright { get; set; }

        /// <summary>
        /// A timestamp of when the package was first created. Fallback property: <see cref="CatalogLeaf.Published"/>.
        /// </summary>
        [JsonPropertyName("created")]
        public DateTimeOffset Created { get; set; }

        /// <summary>
        /// A timestamp of when the package was last edited.
        /// </summary>
        [JsonPropertyName("lastEdited")]
        public DateTimeOffset LastEdited { get; set; }

        /// <summary>
        /// The dependencies of the package, grouped by target framework.
        /// </summary>
        [JsonPropertyName("dependencyGroups")]
        public List<DependencyGroupItem> DependencyGroups { get; set; }

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
        /// Whether or not the package is prerelease. Can be detected from <see cref="CatalogLeaf.PackageVersion"/>.
        /// Note that the NuGet.org catalog had this wrong in some cases.
        /// Example: https://api.nuget.org/v3/catalog0/data/2016.03.11.21.02.55/mvid.fody.2.json
        /// </summary>
        [JsonPropertyName("isPrerelease")]
        public bool IsPrerelease { get; set; }

        /// <summary>
        /// The package's language.
        /// </summary>
        [JsonPropertyName("language")]
        public string Language { get; set; }

        /// <summary>
        /// THe URL to the package's license.
        /// </summary>
        [JsonPropertyName("licenseUrl")]
        public string LicenseUrl { get; set; }

        /// <summary>
        /// Whether the pacakge is listed.
        /// </summary>
        [JsonPropertyName("listed")]
        public bool? Listed { get; set; }

        /// <summary>
        /// The minimum NuGet client version needed to use this package.
        /// </summary>
        [JsonPropertyName("minClientVersion")]
        public string MinClientVersion { get; set; }

        /// <summary>
        /// The hash of the package encoded using Base64.
        /// Hash algorithm can be detected using <see cref="PackageHashAlgorithm"/>.
        /// </summary>
        [JsonPropertyName("packageHash")]
        public string PackageHash { get; set; }

        /// <summary>
        /// The algorithm used to hash <see cref="PackageHash"/>.
        /// </summary>
        [JsonPropertyName("packageHashAlgorithm")]
        public string PackageHashAlgorithm { get; set; }

        /// <summary>
        /// The size of the package .nupkg in bytes.
        /// </summary>
        [JsonPropertyName("packageSize")]
        public long PackageSize { get; set; }

        /// <summary>
        /// The URL for the package's home page.
        /// </summary>
        [JsonPropertyName("projectUrl")]
        public string ProjectUrl { get; set; }

        /// <summary>
        /// The package's release notes.
        /// </summary>
        [JsonPropertyName("releaseNotes")]
        public string ReleaseNotes { get; set; }

        /// <summary>
        /// If true, the package requires its license to be accepted.
        /// </summary>
        [JsonPropertyName("requireLicenseAcceptance")]
        public bool? RequireLicenseAcceptance { get; set; }

        /// <summary>
        /// The package's summary.
        /// </summary>
        [JsonPropertyName("summary")]
        public string Summary { get; set; }

        /// <summary>
        /// The package's tags.
        /// </summary>
        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; }

        /// <summary>
        /// The package's title.
        /// </summary>
        [JsonPropertyName("title")]
        public string Title { get; set; }

        /// <summary>
        /// The version string as it's originally found in the .nuspec.
        /// </summary>
        [JsonPropertyName("verbatimVersion")]
        public string VerbatimVersion { get; set; }

        /// <summary>
        /// The package's License Expression.
        /// </summary>
        [JsonPropertyName("licenseExpression")]
        public string LicenseExpression { get; set; }

        /// <summary>
        /// The package's license file.
        /// </summary>
        [JsonPropertyName("licenseFile")]
        public string LicenseFile { get; set; }

        /// <summary>
        /// The package's icon file.
        /// </summary>
        [JsonPropertyName("iconFile")]
        public string IconFile { get; set; }
    }
}
