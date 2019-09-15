using System.Collections.Generic;
using Newtonsoft.Json;

namespace BaGet.Protocol.Models
{
    /// <summary>
    /// The package ids that matched the autocomplete query.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/search-autocomplete-service-resource#search-for-package-ids.
    /// </summary>
    public class AutocompleteResponse
    {
        public AutocompleteContext Context { get; set; }

        /// <summary>
        /// The total number of matches, disregarding skip and take.
        /// </summary>
        [JsonProperty("totalHits")]
        public long TotalHits { get; set; }

        /// <summary>
        /// The package IDs matched by the autocomplete query.
        /// </summary>
        [JsonProperty("data")]
        public IReadOnlyList<string> Data { get; set; }
    }
}
