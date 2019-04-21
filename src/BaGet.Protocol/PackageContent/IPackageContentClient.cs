using System.IO;
using System.Threading.Tasks;

namespace BaGet.Protocol
{
    /// <summary>
    /// A client to interact with the Package Content resource. This resource can be used to download
    /// NuGet packages and fetch other metadata.
    /// Protocol documentation: https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource
    /// </summary>
    public interface IPackageContentClient
    {
        /// <summary>
        /// Get a package's versions, or null if the package does not exist.
        /// </summary>
        /// <param name="url">The URL to fetch the package versions from.</param>
        /// <returns>The package's versions, or null if the package does not exist</returns>
        Task<PackageVersions> GetPackageVersionsOrNullAsync(string url);

        /// <summary>
        /// Download a package.
        /// </summary>
        /// <param name="url">The URL to the package's content.</param>
        /// <returns>The package's content stream. May not be seekable.</returns>
        Task<Stream> GetPackageContentStreamAsync(string url);

        /// <summary>
        /// Download a package's manifest (nuspec).
        /// </summary>
        /// <param name="url">The URL to the package's manifest.</param>
        /// <returns>The package's manifest stream. May not be seekable.</returns>
        Task<Stream> GetPackageManifestStreamAsync(string url);
    }
}
