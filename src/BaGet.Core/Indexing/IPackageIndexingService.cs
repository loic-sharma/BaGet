using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core
{
    /// <summary>
    /// The result of indexing a package.
    /// See: <see cref="IPackageIndexingService.IndexAsync(Stream, CancellationToken)"/>.
    /// </summary>
    public class PackageIndexingResult
    {
        /// <summary>
        /// The status of the indexing operation.
        /// </summary>
        public PackageIndexingStatus Status { get; set; }

        /// <summary>
        /// Messages that should be surfaced to the client.
        /// </summary>
        public List<string> Messages { get; set; }
    }

    /// <summary>
    /// The result of attempting to index a package.
    /// See <see cref="IPackageIndexingService.IndexAsync(Stream, CancellationToken)"/>.
    /// </summary>
    public enum PackageIndexingStatus
    {
        /// <summary>
        /// The package is invalid or malformed. This may also happen if BaGet is in a corrupted state.
        /// </summary>
        InvalidPackage,

        /// <summary>
        /// The package has already been indexed.
        /// </summary>
        PackageAlreadyExists,

        /// <summary>
        /// The service encountered an unexpected error while indexing the package.
        /// </summary>
        UnexpectedError,

        /// <summary>
        /// The package has been indexed successfully.
        /// </summary>
        Success,
    }

    /// <summary>
    /// The service used to accept new packages.
    /// </summary>
    public interface IPackageIndexingService
    {
        /// <summary>
        /// Attempt to index a new package.
        /// </summary>
        /// <param name="stream">The stream containing the package's content.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The result of the indexing operation.</returns>
        Task<PackageIndexingResult> IndexAsync(Stream stream, CancellationToken cancellationToken);
    }
}
