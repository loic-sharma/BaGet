namespace BaGet.Protocol
{
    /// <summary>
    /// Creates and configures clients that interact with a NuGet server.
    /// </summary>
    public interface INuGetClientFactory
    {
        /// <summary>
        /// Create a client to interact with the NuGet Package Content resource.
        /// See: https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource
        /// </summary>
        /// <returns>A client to interact with the NuGet Package Content resource.</returns>
        IPackageContentResource CreatePackageContentClient();

        /// <summary>
        /// Create a client to interact with the NuGet Package Metadata resource.
        /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
        /// </summary>
        /// <returns>A client to interact with the NuGet Package Metadata resource.</returns>
        IPackageMetadataResource CreatePackageMetadataClient();

        /// <summary>
        /// Create a client to interact with the NuGet Search resource.
        /// </summary>
        /// <returns>A client to interact with the NuGet Search resource.</returns>
        ISearchResource CreateSearchClient();
    }
}
