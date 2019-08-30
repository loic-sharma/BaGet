using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    /// <summary>
    /// The Package Metadata resource, used to fetch packages' metadata.
    /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
    /// </summary>
    public interface IPackageMetadataResource
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
        /// Get a page that was linked by the package's registration index, if it exists.
        /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#registration-page
        /// </summary>
        /// <param name="packageId">The package's id.</param>
        /// <param name="lower">The lowest SemVer 2.0.0 version in the page (inclusive).</param>
        /// <param name="upper">The highest SemVer 2.0.0 version in the page (inclusive).</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The registration index page, or null if the page does not exist.</returns>
        Task<RegistrationPageResponse> GetRegistrationPageOrNullAsync(
            string packageId,
            NuGetVersion lower,
            NuGetVersion upper,
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
