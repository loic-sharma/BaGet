using System.Threading.Tasks;

namespace BaGet.Protocol
{
    /// <summary>
    /// A client to interact with the Package Metadata resource. This resource can be used to fetch
    /// a package's metadata.
    /// Protocol documentation: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
    /// </summary>
    public interface IRegistrationClient
    {
        /// <summary>
        /// Attempt to get a package's registration index, if it exists.
        /// </summary>
        /// <param name="indexUrl">The url to load the registration index.</param>
        /// <returns>The package's registration index, or null if the package does not exist</returns>
        Task<RegistrationIndex> GetRegistrationIndexOrNullAsync(string indexUrl);

        /// <summary>
        /// Get a page that was linked by the package's registration index.
        /// </summary>
        /// <param name="pageUrl">The url to load the registration page.</param>
        /// <returns>The registration index page.</returns>
        Task<RegistrationIndexPage> GetRegistrationIndexPageAsync(string pageUrl);

        /// <summary>
        /// Get the metadata for a single package version.
        /// </summary>
        /// <param name="leafUrl">The url to load the registration leaf.</param>
        /// <returns>The registration leaf.</returns>
        Task<RegistrationLeaf> GetRegistrationLeafAsync(string leafUrl);
    }
}
