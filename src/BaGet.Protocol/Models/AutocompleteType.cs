namespace BaGet.Protocol.Models
{
    /// <summary>
    /// The autocomplete request type.
    ///
    /// See https://docs.microsoft.com/en-us/nuget/api/search-autocomplete-service-resource
    /// </summary>
    public enum AutocompleteType
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
