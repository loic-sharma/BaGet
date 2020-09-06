using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BaGet.Protocol.Models
{
    /// <summary>
    /// The package ids that matched the autocomplete query.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/search-autocomplete-service-resource#search-for-package-ids
    /// </summary>
    public class AutocompleteResponse
    {
        [JsonPropertyName("@context")]
        public AutocompleteContext Context { get; set; }

        /// <summary>
        /// The total number of matches, disregarding skip and take.
        /// </summary>
        [JsonPropertyName("totalHits")]
        public long TotalHits { get; set; }

        /// <summary>
        /// The package IDs matched by the autocomplete query.
        /// </summary>
        [JsonPropertyName("data")]
        public IReadOnlyList<string> Data { get; set; }
    }
}
