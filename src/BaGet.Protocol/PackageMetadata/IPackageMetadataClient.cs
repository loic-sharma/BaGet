using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    /// <summary>
    /// The Package Metadata client, used to fetch packages' metadata.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
    /// </summary>
    public interface IPackageMetadataClient
    {
        /// <summary>
        /// Attempt to get a package's registration index, if it exists.
        /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#registration-page
        /// </summary>
        /// <param name="packageId">The package's ID.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The package's registration index, or null if the package does not exist</returns>
        Task<RegistrationIndexResponse> GetRegistrationIndexOrNullAsync(string packageId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get a page that was linked from the package's registration index.
        /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#registration-page
        /// </summary>
        /// <param name="pageUrl">The URL of the page, from the <see cref="RegistrationIndexResponse"/>.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The registration index page, or null if the page does not exist.</returns>
        Task<RegistrationPageResponse> GetRegistrationPageAsync(
            string pageUrl,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Get the metadata for a single package version, if the package exists.
        /// </summary>
        /// <param name="packageId">The package's id.</param>
        /// <param name="packageVersion">The package's version.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The registration leaf, or null if the package does not exist.</returns>
        Task<RegistrationLeafResponse> GetRegistrationLeafOrNullAsync(
            string packageId,
            NuGetVersion packageVersion,
            CancellationToken cancellationToken = default);
    }
}
