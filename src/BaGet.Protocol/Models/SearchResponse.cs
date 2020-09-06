using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BaGet.Protocol.Models
{
    /// <summary>
    /// The response to a search query.
    ///
    /// See https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource#response
    /// </summary>
    public class SearchResponse
    {
        [JsonPropertyName("@context")]
        public SearchContext Context { get; set; }

        /// <summary>
        /// The total number of matches, disregarding skip and take.
        /// </summary>
        [JsonPropertyName("totalHits")]
        public long TotalHits { get; set; }

        /// <summary>
        /// The packages that matched the search query.
        /// </summary>
        [JsonPropertyName("data")]
        public IReadOnlyList<SearchResult> Data { get; set; }
    }
}
