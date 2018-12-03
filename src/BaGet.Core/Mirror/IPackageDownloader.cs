using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core.Mirror
{
    public interface IPackageDownloader
    {
        /// <summary>
        /// Attempt to download a package.
        /// </summary>
        /// <param name="packageUri">The package to download.</param>
        /// <param name="cancellationToken">The token to cancel the download.</param>
        /// <returns>The package, or null if it couldn't be downloaded.</returns>
        Task<Stream> DownloadOrNullAsync(Uri packageUri, CancellationToken cancellationToken);
    }
}
