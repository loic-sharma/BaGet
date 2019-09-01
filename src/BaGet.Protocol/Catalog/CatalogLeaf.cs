using System;
using BaGet.Protocol.Internal;
using Newtonsoft.Json;

namespace BaGet.Protocol
{
    /// <summary>
    /// A catalog leaf. Represents a single package event.
    /// Leafs can be discovered from a <see cref="CatalogPage"/>.
    /// See: https://docs.microsoft.com/en-us/nuget/api/catalog-resource#catalog-leaf
    /// </summary>
    public class CatalogLeaf : ICatalogLeafItem
    {
        [JsonProperty("@id")]
        public string Url { get; set; }

        [JsonProperty("@type")]
        [JsonConverter(typeof(CatalogLeafTypeConverter))]
        public CatalogLeafType Type { get; set; }

        [JsonProperty("catalog:commitId")]
        public string CommitId { get; set; }

        [JsonProperty("catalog:commitTimeStamp")]
        public DateTimeOffset CommitTimestamp { get; set; }

        [JsonProperty("id")]
        public string PackageId { get; set; }

        [JsonProperty("published")]
        public DateTimeOffset Published { get; set; }

        [JsonProperty("version")]
        public string PackageVersion { get; set; }
    }
}
