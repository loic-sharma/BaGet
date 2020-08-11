using System.Collections.Generic;
using System.Text.Json.Serialization;

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
        [JsonPropertyName("totalHits")]
        public long TotalHits { get; set; }

        /// <summary>
        /// The package IDs matched by the dependent query.
        /// </summary>
        [JsonPropertyName("data")]
        public IReadOnlyList<DependentResponse> Data { get; set; }
    }

    /// <summary>
    /// A package that depends on the queried package.
    /// </summary>
    public class DependentResponse
    {
        [JsonPropertyName("key")]
        public int Key { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("totalDownloads")]
        public long TotalDownloads { get; set; }
    }
}
