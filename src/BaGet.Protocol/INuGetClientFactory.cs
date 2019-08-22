namespace BaGet.Protocol
{
    /// <summary>
    /// Creates clients to interact with a NuGet server.
    /// </summary>
    public interface INuGetClientFactory
    {
        /// <summary>
        /// Create a  low-level client to interact with the NuGet Service Index resource.
        /// See: https://docs.microsoft.com/en-us/nuget/api/service-index
        /// </summary>
        /// <returns>A client to interact with the NuGet Service Index resource.</returns>
        IServiceIndexResource CreateServiceIndexClient();

        /// <summary>
        /// Create a low-level client to interact with the NuGet Package Content resource.
        /// See: https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource
        /// </summary>
        /// <returns>A client to interact with the NuGet Package Content resource.</returns>
        IPackageContentResource CreatePackageContentClient();

        /// <summary>
        /// Create a low-level client to interact with the NuGet Package Metadata resource.
        /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
        /// </summary>
        /// <returns>A client to interact with the NuGet Package Metadata resource.</returns>
        IPackageMetadataResource CreatePackageMetadataClient();

        /// <summary>
        /// Create a low-level client to interact with the NuGet Search resource.
        /// See: https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource
        /// </summary>
        /// <returns>A client to interact with the NuGet Search resource.</returns>
        ISearchResource CreateSearchClient();
    }
}
