using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core
{
    /// <summary>
    /// A single indexing step for <see cref="IPackageIndexingService.IndexAsync(Stream, CancellationToken)"/>.
    /// </summary>
    public interface IPackageIndexingMiddleware
    {
        /// <summary>
        /// Attempt to index a package.
        /// </summary>
        /// <param name="context">The context for the package that is being indexed.</param>
        /// <param name="next">The next middleware in the indexing chain.</param>
        /// <returns>A task that completes when the package has been indexed.</returns>
        Task IndexAsync(PackageIndexingContext context, PackageIndexingDelegate next);
    }

    /// <summary>
    /// A delegate that wraps a <see cref="IPackageIndexingMiddleware"/>.
    /// </summary>
    /// <returns>A task that completes when the middleware is done indexing the package.</returns>
    public delegate Task PackageIndexingDelegate();

    /// <summary>
    /// The state of the package indexing operation.
    /// </summary>
    public class PackageIndexingContext : PackageIndexingResult, IDisposable
    {
        /// <summary>
        /// The metadata for the package that is being indexed.
        /// </summary>
        public Package Package { get; set; }

        /// <summary>
        /// The content stream for the package that is being indexed.
        /// </summary>
        public Stream PackageStream { get; set; }

        /// <summary>
        /// The package manifest content stream for the package that is being indexed.
        /// </summary>
        public Stream NuspecStream { get; set; }

        /// <summary>
        /// The embedded icon content stream for the package that is being indexed.
        /// Null if the package does not have an embedded icon.
        /// </summary>
        public Stream IconStream { get; set; }

        /// <summary>
        /// The embedded readme content stream for the package that is being indexed.
        /// Null if the package does not have an embedded readme.
        /// </summary>
        public Stream ReadmeStream { get; set; }

        /// <summary>
        /// The token to cancel the indexing operation.
        /// </summary>
        public CancellationToken CancellationToken { get; set; }

        public void Dispose()
        {
            PackageStream?.Dispose();
            NuspecStream?.Dispose();
            IconStream?.Dispose();
            ReadmeStream?.Dispose();
        }
    }
}
