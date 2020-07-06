using System;
using System.Text.Json.Serialization;

namespace BaGet.Protocol.Models
{
    // This class is based off https://github.com/NuGet/NuGet.Services.Metadata/blob/64af0b59c5a79e0143f0808b39946df9f16cb2e7/src/NuGet.Protocol.Catalog/Models/CatalogLeafItem.cs

    /// <summary>
    /// An item in a <see cref="CatalogPage"/> that references a <see cref="CatalogLeaf"/>.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/catalog-resource#catalog-item-object-in-a-page
    /// </summary>
    public class CatalogLeafItem : ICatalogLeafItem
    {
        /// <summary>
        /// The URL to the current catalog leaf.
        /// </summary>
        [JsonPropertyName("@id")]
        public string CatalogLeafUrl { get; set; }

        /// <summary>
        /// The type of the current catalog leaf.
        /// </summary>
        [JsonPropertyName("@type")]
        public string Type { get; set; }

        /// <summary>
        /// The commit timestamp of this catalog item.
        /// </summary>
        [JsonPropertyName("commitTimeStamp")]
        public DateTimeOffset CommitTimestamp { get; set; }

        /// <summary>
        /// The package ID of the catalog item.
        /// </summary>
        [JsonPropertyName("nuget:id")]
        public string PackageId { get; set; }

        /// <summary>
        /// The package version of the catalog item.
        /// </summary>
        [JsonPropertyName("nuget:version")]
        public string PackageVersion { get; set; }
    }
}
