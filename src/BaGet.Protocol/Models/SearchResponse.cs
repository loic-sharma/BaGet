using System.Collections.Generic;
using Newtonsoft.Json;

namespace BaGet.Protocol.Models
{
    /// <summary>
    /// The response to a search query.
    /// Documentation: https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource#response
    /// </summary>
    public class SearchResponse
    {
        [JsonProperty("@context")]
        public SearchContext Context { get; set; }

        /// <summary>
        /// The total number of matches, disregarding skip and take.
        /// </summary>
        [JsonProperty("totalHits")]
        public long TotalHits { get; set; }

        /// <summary>
        /// The packages that matched the search query.
        /// </summary>
        [JsonProperty("data")]
        public IReadOnlyList<SearchResult> Data { get; set; }
    }
}
