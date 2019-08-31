using System;
using BaGet.Protocol.Internal;
using Newtonsoft.Json;

namespace BaGet.Protocol
{
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
