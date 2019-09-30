using System.Collections.Generic;
using Newtonsoft.Json;

namespace BaGet.Core
{
    /// <summary>
    /// The package ids that depend on the queried package.
    /// This is an unofficial API that isn't part of the NuGet protocol.
    /// </summary>
    public class DependentsResponse
    {
        /// <summary>
        /// The total number of matches, disregarding skip and take.
        /// </summary>
        [JsonProperty("totalHits")]
        public long TotalHits { get; set; }

        /// <summary>
        /// The package IDs matched by the dependent query.
        /// </summary>
        [JsonProperty("data")]
        public IReadOnlyList<string> Data { get; set; }
    }
}
