using System;
using BaGet.Protocol.Internal;
using Newtonsoft.Json;

namespace BaGet.Protocol
{
    /// <summary>
    /// An item in a <see cref="CatalogPage"/> that references a <see cref="CatalogLeaf"/>.
    /// See: https://docs.microsoft.com/en-us/nuget/api/catalog-resource#catalog-item-object-in-a-page
    /// Based off: https://github.com/NuGet/NuGet.Services.Metadata/blob/64af0b59c5a79e0143f0808b39946df9f16cb2e7/src/NuGet.Protocol.Catalog/Models/CatalogLeafItem.cs
    /// </summary>
    public class CatalogLeafItem : ICatalogLeafItem
    {
        [JsonProperty("@id")]
        public string Url { get; set; }

        [JsonProperty("@type")]
        [JsonConverter(typeof(CatalogLeafItemTypeConverter))]
        public CatalogLeafType Type { get; set; }

        [JsonProperty("commitTimeStamp")]
        public DateTimeOffset CommitTimestamp { get; set; }

        [JsonProperty("nuget:id")]
        public string PackageId { get; set; }

        [JsonProperty("nuget:version")]
        public string PackageVersion { get; set; }
    }
}
