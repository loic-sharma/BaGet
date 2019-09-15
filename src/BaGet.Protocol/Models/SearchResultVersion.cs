using Newtonsoft.Json;

namespace BaGet.Protocol.Models
{
    /// <summary>
    /// The version of a package that matched a search query.
    /// See: <see cref="SearchResult"/>.
    /// Documentation: https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource#search-result
    /// </summary>
    public class SearchResultVersion
    {
        [JsonProperty("@id")]
        public string RegistrationLeafUrl { get; set; }

        /// <summary>
        /// The full NuGet version after normalization, including any SemVer 2.0.0 build metadata.
        /// </summary>
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("downloads")]
        public long Downloads { get; set; }
    }
}
