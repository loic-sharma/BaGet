namespace BaGet.Core
{
    /// <summary>
    /// The NuGet V3 enumerage package versions request.
    /// See: https://docs.microsoft.com/en-us/nuget/api/search-autocomplete-service-resource#request-parameters-1
    /// </summary>
    public class VersionsRequest
    {
        /// <summary>
        /// Whether to include pre-release packages.
        /// </summary>
        public bool IncludePrerelease { get; set; }

        /// <summary>
        /// Whether to include SemVer 2.0.0 compatible packages.
        /// </summary>
        public bool IncludeSemVer2 { get; set; }

        /// <summary>
        /// Filter results to a package type. If null, no filter is applied.
        /// </summary>
        public string PackageType { get; set; }

        /// <summary>
        /// The search query.
        /// </summary>
        public string Query { get; set; }
    }
}
