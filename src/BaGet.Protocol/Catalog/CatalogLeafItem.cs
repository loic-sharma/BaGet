using System;
using BaGet.Protocol.Internal;
using Newtonsoft.Json;

namespace BaGet.Protocol
{
    /// <summary>
    /// An item in a <see cref="CatalogPage"/> that references a <see cref="CatalogLeaf"/>.
    /// See: https://docs.microsoft.com/en-us/nuget/api/catalog-resource#catalog-item-object-in-a-page
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
