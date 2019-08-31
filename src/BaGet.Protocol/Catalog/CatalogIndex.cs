using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BaGet.Protocol
{
    public class CatalogIndex
    {
        [JsonProperty("commitTimeStamp")]
        public DateTimeOffset CommitTimestamp { get; set; }

        [JsonProperty("count")]
        public int Count { get; set; }

        [JsonProperty("items")]
        public List<CatalogPageItem> Items { get; set; }
    }
}
