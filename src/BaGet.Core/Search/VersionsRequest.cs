namespace BaGet.Core
{
    /// <summary>
    /// The NuGet V3 enumerate package versions request.
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
        /// The package ID whose versions should be fetched.
        /// </summary>
        public string PackageId { get; set; }
    }
}
