using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BaGet.Protocol
{
    /// <summary>
    /// The catalog index is the entry point for the catalog resource.
    /// Use this to discover catalog pages, which in turn can be used to discover catalog leafs.
    /// See: https://docs.microsoft.com/en-us/nuget/api/catalog-resource#catalog-index
    /// </summary>
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
