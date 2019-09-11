namespace BaGet.Protocol
{
    /// <summary>
    /// The autocomplete request type.
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
