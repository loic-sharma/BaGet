using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core
{
    /// <summary>
    /// A low-level storage abstraction.
    /// </summary>
    /// <remarks>
    /// This is used to store:
    ///
    /// * Packages, through <see cref="PackageStorageService"/>
    /// * Symbols, through <see cref="SymbolStorageService"/>
    ///
    /// This storage abstraction has implementations for disk,
    /// Azure Blob Storage, Amazon Web Services S3, and Google Cloud Storage.
    /// </remarks>
    public interface IStorageService
    {
        /// <summary>
        /// Get content from storage.
        /// </summary>
        /// <param name="path">The content's path.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The path's content or null if the content does not exist.</returns>
        Task<Stream> GetAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get a URI that can be used to download the content.
        /// </summary>
        /// <param name="path">The content's path.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>
        /// The content's URI. This may be a local file. Returns null if the content
        /// does not exist or if a URI could not be created.
        /// </returns>
        Task<Uri> GetDownloadUriAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Store content into storage.
        /// </summary>
        /// <param name="path">The path at which to store the content.</param>
        /// <param name="content">The content to store at the given path.</param>
        /// <param name="contentType">The type of content that is being stored.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The result of the put operation.</returns>
        Task<StoragePutResult> PutAsync(
            string path,
            Stream content,
            string contentType,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Remove content from storage.
        /// </summary>
        /// <param name="path">The path to the content to delete.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>A task that completes when the content has been deleted.</returns>
        Task DeleteAsync(string path, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The result of a <see cref="IStorageService.PutAsync(string, Stream, string, CancellationToken)"/> operation.
    /// </summary>
    public enum StoragePutResult
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
}
