using System;
using BaGet.Protocol.Internal;
using Newtonsoft.Json;

namespace BaGet.Protocol.Models
{
    // This classed is based off: https://github.com/NuGet/NuGet.Services.Metadata/blob/64af0b59c5a79e0143f0808b39946df9f16cb2e7/src/NuGet.Protocol.Catalog/Models/CatalogLeaf.cs

    /// <summary>
    /// A catalog leaf. Represents a single package event.
    /// Leafs can be discovered from a <see cref="CatalogPage"/>.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/catalog-resource#catalog-leaf
    /// </summary>
    public class CatalogLeaf : ICatalogLeafItem
    {
        /// <summary>
        /// The URL to the current catalog leaf.
        /// </summary>
        [JsonProperty("@id")]
        public string CatalogLeafUrl { get; set; }

        /// <summary>
        /// The type of the current catalog leaf.
        /// </summary>
        [JsonProperty("@type")]
        [JsonConverter(typeof(CatalogLeafTypeConverter))]
        public CatalogLeafType Type { get; set; }

        /// <summary>
        /// The catalog commit ID associated with this catalog item.
        /// </summary>
        [JsonProperty("catalog:commitId")]
        public string CommitId { get; set; }

        /// <summary>
        /// The commit timestamp of this catalog item.
        /// </summary>
        [JsonProperty("catalog:commitTimeStamp")]
        public DateTimeOffset CommitTimestamp { get; set; }

        /// <summary>
        /// The package ID of the catalog item.
        /// </summary>
        [JsonProperty("id")]
        public string PackageId { get; set; }

        /// <summary>
        /// The published date of the package catalog item.
        /// </summary>
        [JsonProperty("published")]
        public DateTimeOffset Published { get; set; }

        /// <summary>
        /// The package version of the catalog item.
        /// </summary>
        [JsonProperty("version")]
        public string PackageVersion { get; set; }
    }
}
