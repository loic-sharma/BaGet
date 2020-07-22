using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BaGet.Protocol.Models
{
    // This class is based off https://github.com/NuGet/NuGet.Services.Metadata/blob/64af0b59c5a79e0143f0808b39946df9f16cb2e7/src/NuGet.Protocol.Catalog/Models/CatalogPage.cs

    /// <summary>
    /// A catalog page, used to discover catalog leafs.
    /// Pages can be discovered from a <see cref="CatalogIndex"/>.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/catalog-resource#catalog-page
    /// </summary>
    public class CatalogPage
    {
        /// <summary>
        /// A unique ID associated with the most recent commit in this page.
        /// </summary>
        [JsonPropertyName("commitTimeStamp")]
        public DateTimeOffset CommitTimestamp { get; set; }

        /// <summary>
        /// The number of items in the page.
        /// </summary>
        [JsonPropertyName("count")]
        public int Count { get; set; }

        /// <summary>
        /// The items used to discover <see cref="CatalogLeaf"/>s.
        /// </summary>
        [JsonPropertyName("items")]
        public List<CatalogLeafItem> Items { get; set; }

        /// <summary>
        /// The URL to the Catalog Index.
        /// </summary>
        [JsonPropertyName("parent")]
        public string CatalogIndexUrl { get; set; }
    }
}
