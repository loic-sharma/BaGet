using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core
{
    /// <summary>
    /// The result of attempting to index a symbol package.
    /// See <see cref="ISymbolIndexingService.IndexAsync(Stream, CancellationToken)"/>.
    /// </summary>
    public enum SymbolIndexingResult
    {
        /// <summary>
        /// The symbol package is malformed.
        /// </summary>
        InvalidSymbolPackage,

        /// <summary>
        /// A corresponding package with the provided ID and version does not exist.
        /// </summary>
        PackageNotFound,

        /// <summary>
        /// The symbol package has been indexed successfully.
        /// </summary>
        Success,
    }

    /// <summary>
    /// The service used to accept new symbol packages.
    /// </summary>
    public interface ISymbolIndexingService
    {
        /// <summary>
        /// Attempt to index a new symbol package.
        /// </summary>
        /// <param name="stream">The stream containing the symbol package's content.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The result of the attempted indexing operation.</returns>
        Task<SymbolIndexingResult> IndexAsync(Stream stream, CancellationToken cancellationToken);
    }
}
