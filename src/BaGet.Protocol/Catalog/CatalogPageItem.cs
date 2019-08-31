using System;
using Newtonsoft.Json;

namespace BaGet.Protocol
{
    public class CatalogPageItem
    {
        [JsonProperty("@id")]
        public string Url { get; set; }

        [JsonProperty("commitTimeStamp")]
        public DateTimeOffset CommitTimestamp { get; set; }

        [JsonProperty("count")]
        public int Count { get; set; }
    }
}
