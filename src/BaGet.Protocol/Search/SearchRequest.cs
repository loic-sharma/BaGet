namespace BaGet.Protocol
{
    /// <summary>
    /// The request parameters for a search query.
    /// See: https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource#request-parameters
    /// </summary>
    public class SearchRequest
    {
        /// <summary>
        /// How many results to skip.
        /// </summary>
        public int Skip { get; set; }

        /// <summary>
        /// How many results to return.
        /// </summary>
        public int Take { get; set; }

        /// <summary>
        /// Whether pre-release packages should be returned.
        /// </summary>
        public bool IncludePrerelease { get; set; }

        /// <summary>
        /// Whether packages that require SemVer 2.0.0 compatibility should be returned.
        /// </summary>
        public bool IncludeSemVer2 { get; set; } = true;

        /// <summary>
        /// The search query.
        /// </summary>
        public string Query { get; set; }
    }
}
