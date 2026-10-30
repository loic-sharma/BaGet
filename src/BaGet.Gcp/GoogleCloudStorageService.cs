using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Google;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Options;

namespace BaGet.Gcp;

public partial class GoogleCloudStorageService : IStreamingStorageService, IListableStorageService
{
    private readonly string? _bucketName;
    private readonly Func<Task<StorageClient>> _clientFactory;

    public GoogleCloudStorageService(IOptionsSnapshot<GoogleCloudStorageOptions> options)
        : this(options, () => StorageClient.CreateAsync())
    {
    }

    /// <summary>Builds the service over a client other than the default one (an emulator, in tests).</summary>
    internal GoogleCloudStorageService(IOptionsSnapshot<GoogleCloudStorageOptions> options, Func<Task<StorageClient>> clientFactory)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        _bucketName = options.Value.BucketName;
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    private Task<StorageClient> CreateClientAsync() => _clientFactory();

    public async Task<Stream> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        // Spooled to a temporary file (deleted on dispose), not to a MemoryStream: the object is never held in managed memory.
        using (var storage = await CreateClientAsync().ConfigureAwait(false))
        {
            var file = new FileStream(
                Path.Combine(Path.GetTempPath(), "baget-gcs-" + Guid.NewGuid().ToString("N")),
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                81920,
                FileOptions.DeleteOnClose | FileOptions.Asynchronous);
            try
            {
                await storage.DownloadObjectAsync(_bucketName, CoercePath(path), file, cancellationToken: cancellationToken).ConfigureAwait(false);
                file.Position = 0;
                return file;
            }
            catch (GoogleApiException e) when (e.HttpStatusCode == HttpStatusCode.NotFound)
            {
                await file.DisposeAsync().ConfigureAwait(false);

                // The one "missing content" exception of IStorageService.GetAsync, as FileStorageService throws.
                throw new FileNotFoundException("The object '" + path + "' does not exist.", path, e);
            }
            catch
            {
                await file.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    public Task<Uri> GetDownloadUriAsync(string path, CancellationToken cancellationToken = default)
    {
        // returns an Authenticated Browser Download URL: https://cloud.google.com/storage/docs/request-endpoints#cookieauth
        return Task.FromResult(new Uri($"https://storage.googleapis.com/{_bucketName}/{CoercePath(path).TrimStart('/')}"));
    }

    public async Task<StoragePutResult> PutAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        using (var storage = await CreateClientAsync().ConfigureAwait(false))
        // A temporary file, not a MemoryStream: a large package is never held in managed memory.
        using (var seekableContent = await StreamingStreams.SpoolAsync(content, cancellationToken).ConfigureAwait(false))
        {

            var objectName = CoercePath(path);

            try
            {
                // attempt to upload, succeeding only if the object doesn't exist
                await storage.UploadObjectAsync(_bucketName, objectName, contentType, seekableContent, new UploadObjectOptions { IfGenerationMatch = 0 }, cancellationToken);
                return StoragePutResult.Success;
            }
            catch (GoogleApiException e) when (e.HttpStatusCode == HttpStatusCode.PreconditionFailed)
            {
                // the object already exists; get the hash of its content from its metadata
                var existingObject = await storage.GetObjectAsync(_bucketName, objectName, cancellationToken: cancellationToken);
                var existingHash = Convert.FromBase64String(existingObject.Md5Hash);

                // hash the content that was uploaded
                seekableContent.Position = 0;
                byte[] contentHash;
                using (var md5 = MD5.Create())
                    contentHash = md5.ComputeHash(seekableContent);

                // conflict if the two hashes are different
                return existingHash.SequenceEqual(contentHash) ? StoragePutResult.AlreadyExists : StoragePutResult.Conflict;
            }
        }
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        using (var storage = await CreateClientAsync())
        {
            try
            {
                var obj = await storage.GetObjectAsync(_bucketName, CoercePath(path), cancellationToken: cancellationToken);
                await storage.DeleteObjectAsync(obj, cancellationToken: cancellationToken);
            }
            catch (GoogleApiException e) when (e.HttpStatusCode == HttpStatusCode.NotFound)
            {
            }
        }
    }

    /// <summary>
    /// Every object under a prefix. The sweep of abandoned staged uploads is the only
    /// caller, and without this a bucket would keep them forever.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListAsync(string pathPrefix, CancellationToken cancellationToken = default)
    {
        // A prefix is a path segment, not a string prefix: "staging" must not also match
        // "staging-archive/...".
        var prefix = CoercePath(pathPrefix).TrimEnd('/') + "/";
        var paths = new List<string>();

        using (var storage = await CreateClientAsync())
        {
            // The object's name is already the path IStorageService takes.
            await foreach (var item in storage.ListObjectsAsync(_bucketName, prefix).WithCancellation(cancellationToken))
            {
                paths.Add(item.Name);
            }
        }

        return paths;
    }

    private static string CoercePath(string path)
    {
        // although Google Cloud Storage objects exist in a flat namespace, using forward slashes allows the objects to
        // be exposed as nested subdirectories, e.g., when browsing via Google Cloud Console
        return path.Replace('\\', '/');
    }
}