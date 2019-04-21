namespace BaGet.Protocol
{
    /// <summary>
    /// Search for packages in an autocomplete scenario.
    /// See: https://docs.microsoft.com/en-us/nuget/api/search-autocomplete-service-resource#request-parameters
    /// </summary>
    public class AutocompleteRequest : SearchRequest
    {
        /// <summary>
        /// The autocomplete request type.
        /// </summary>
        public AutocompleteRequestType Type { get; set; }
    }

    /// <summary>
    /// The autocomplete request type.
    /// </summary>
    public enum AutocompleteRequestType
    {
        /// <summary>
        /// The response's data should list matching package IDs.
        /// </summary>
        PackageIds,

        /// <summary>
        /// The response should list the package's versions.
        /// </summary>
        PackageVersions,
    }
}
