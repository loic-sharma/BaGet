using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core.Services
{
    /// <summary>
    /// The result of a <see cref="IStorageService.PutAsync(string, Stream, string)"/> operation.
    /// </summary>
    public enum PutResult
    {
        /// <summary>
        /// The given path is already used to store different content.
        /// </summary>
        Conflict,

        /// <summary>
        /// This content is already stored at the given path.
        /// </summary>
        AlreadyExists,

        /// <summary>
        /// The content was sucessfully stored.
        /// </summary>
        Success,
    }

    public interface IStorageService
    {
        /// <summary>
        /// Get content from storage.
        /// </summary>
        /// <param name="path">The content's path.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The path's content.</returns>
        Task<Stream> GetAsync(string path, CancellationToken cancellationToken);

        /// <summary>
        /// Get a URI that can be used to download the content.
        /// </summary>
        /// <param name="path">The content's path.</param>
        /// <returns>The content's URI. This may be a local file.</returns>
        Task<Uri> GetDownloadUriAsync(string path, CancellationToken cancellationToken);

        /// <summary>
        /// Store content into storage.
        /// </summary>
        /// <param name="path">The path at which to store the content.</param>
        /// <param name="content">The content to store at the given path.</param>
        /// <param name="contentType">The type of content that is being stored.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The result of the put operation.</returns>
        Task<PutResult> PutAsync(string path, Stream content, string contentType, CancellationToken cancellationToken);

        /// <summary>
        /// Remove content from storage.
        /// </summary>
        /// <param name="path">The path to the content to delete.</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task DeleteAsync(string path, CancellationToken cancellationToken);
    }
}
