using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BaGet.Protocol
{
    /// <summary>
    /// A catalog page, used to discover catalog leafs.
    /// Pages can be discovered from a <see cref="CatalogIndex"/>.
    /// See: https://docs.microsoft.com/en-us/nuget/api/catalog-resource#catalog-page
    /// </summary>
    public class CatalogPage
    {
        [JsonProperty("commitTimeStamp")]
        public DateTimeOffset CommitTimestamp { get; set; }

        [JsonProperty("count")]
        public int Count { get; set; }

        [JsonProperty("items")]
        public List<CatalogLeafItem> Items { get; set; }

        [JsonProperty("parent")]
        public string Parent { get; set; }
    }
}
