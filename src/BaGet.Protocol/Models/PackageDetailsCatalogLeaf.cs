using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BaGet.Protocol
{
    /// <summary>
    /// A "package details" catalog leaf. Represents a single package create or update event.
    /// Leafs can be discovered from a <see cref="CatalogPage"/>.
    /// See: https://docs.microsoft.com/en-us/nuget/api/catalog-resource#catalog-leaf
    /// Based off: https://github.com/NuGet/NuGet.Services.Metadata/blob/64af0b59c5a79e0143f0808b39946df9f16cb2e7/src/NuGet.Protocol.Catalog/Models/PackageDetailsCatalogLeaf.cs
    /// </summary>
    public class PackageDetailsCatalogLeaf : CatalogLeaf
    {
        [JsonProperty("authors")]
        public string Authors { get; set; }

        [JsonProperty("copyright")]
        public string Copyright { get; set; }

        [JsonProperty("created")]
        public DateTimeOffset Created { get; set; }

        [JsonProperty("lastEdited")]
        public DateTimeOffset LastEdited { get; set; }

        [JsonProperty("dependencyGroups")]
        public List<DependencyGroupItem> DependencyGroups { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("iconUrl")]
        public string IconUrl { get; set; }

        /// <summary>
        /// Note that an old bug in the NuGet.org catalog had this wrong in some cases.
        /// Example: https://api.nuget.org/v3/catalog0/data/2016.03.11.21.02.55/mvid.fody.2.json
        /// </summary>
        [JsonProperty("isPrerelease")]
        public bool IsPrerelease { get; set; }

        [JsonProperty("language")]
        public string Language { get; set; }

        [JsonProperty("licenseUrl")]
        public string LicenseUrl { get; set; }

        [JsonProperty("listed")]
        public bool? Listed { get; set; }

        [JsonProperty("minClientVersion")]
        public string MinClientVersion { get; set; }

        [JsonProperty("packageHash")]
        public string PackageHash { get; set; }

        [JsonProperty("packageHashAlgorithm")]
        public string PackageHashAlgorithm { get; set; }

        [JsonProperty("packageSize")]
        public long PackageSize { get; set; }

        [JsonProperty("projectUrl")]
        public string ProjectUrl { get; set; }

        [JsonProperty("releaseNotes")]
        public string ReleaseNotes { get; set; }

        [JsonProperty("requireLicenseAgreement")]
        public bool? RequireLicenseAgreement { get; set; }

        [JsonProperty("summary")]
        public string Summary { get; set; }

        [JsonProperty("tags")]
        public List<string> Tags { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("verbatimVersion")]
        public string VerbatimVersion { get; set; }

        [JsonProperty("licenseExpression")]
        public string LicenseExpression { get; set; }

        [JsonProperty("licenseFile")]
        public string LicenseFile { get; set; }

        [JsonProperty("iconFile")]
        public string IconFile { get; set; }
    }
}
