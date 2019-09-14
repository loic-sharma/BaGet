using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BaGet.Protocol
{
    /// <summary>
    /// A catalog page, used to discover catalog leafs.
    /// Pages can be discovered from a <see cref="CatalogIndex"/>.
    /// See: https://docs.microsoft.com/en-us/nuget/api/catalog-resource#catalog-page
    /// Based off: https://github.com/NuGet/NuGet.Services.Metadata/blob/64af0b59c5a79e0143f0808b39946df9f16cb2e7/src/NuGet.Protocol.Catalog/Models/CatalogPage.cs
    /// </summary>
    public class CatalogPage
    {
        [JsonProperty("commitTimeStamp")]
        public DateTimeOffset CommitTimestamp { get; set; }

        [JsonProperty("count")]
        public int Count { get; set; }

        [JsonProperty("items")]
        public List<CatalogLeafItem> Items { get; set; }

        /// <summary>
        /// The URL to the Catalog Index.
        /// </summary>
        [JsonProperty("parent")]
        public string CatalogIndexUrl { get; set; }
    }
}
