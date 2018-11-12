using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    /// <summary>
    /// Gets metadata about a package from a remote feed.
    /// </summary>
    public interface IPackageMetadataService
    {
        /// <summary>
        /// Get all versions of a package from a remote NuGet feed.
        /// </summary>
        /// <param name="packageId">The package whose versions should be fetched.</param>
        /// <param name="includeUnlisted">Whether results should include unlisted versions.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>All versions for the package.</returns>
        Task<IReadOnlyList<NuGetVersion>> GetAllVersionsAsync(
            string packageId,
            bool includeUnlisted = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Get the URI to download a package's content from a remote NuGet feed.
        /// </summary>
        /// <param name="packageId">The package whose URL should be fetched.</param>
        /// <param name="version">The package's version whose uRL should be fetched.</param>
        /// <returns>A URI that can be used to download the package.</returns>
        Task<Uri> GetPackageContentUriAsync(string packageId, NuGetVersion version);
    }
}
