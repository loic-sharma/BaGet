using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using BaGet.Core;
using Microsoft.Extensions.Options;

namespace BaGet.Aws;

public partial class S3StorageService : IStreamingStorageService, IListableStorageService
{
    private const string Separator = "/";

    /// <summary>
    /// The value S3 expects in the <c>If-None-Match</c> header to mean "only if the key
    /// is unused". See <see cref="PutObjectRequest.IfNoneMatch"/>.
    /// </summary>
    private const string IfKeyIsUnused = "*";

    // See: https://github.com/dotnet/corefx/blob/master/src/Common/src/CoreLib/System/IO/Stream.cs#L35
    private const int DefaultCopyBufferSize = 81920;

    private readonly string? _bucket;
    private readonly string? _prefix;
    private readonly AmazonS3Client _client;

    public S3StorageService(IOptionsSnapshot<S3StorageOptions> options, AmazonS3Client client)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        _bucket = options.Value.Bucket;
        _prefix = options.Value.Prefix;
        _client = client ?? throw new ArgumentNullException(nameof(client));

        if (!string.IsNullOrEmpty(_prefix) && !_prefix.EndsWith(Separator))
            _prefix += Separator;
    }

    private string PrepareKey(string path)
    {
        return _prefix + path.Replace("\\", Separator);
    }

    public async Task<Stream> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        // The response stream is handed out as it is (disposing it releases the connection): the object is never held in memory.
        GetObjectResponse response;
        try
        {
            response = await _client.GetObjectAsync(_bucket, PrepareKey(path), cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            // The one "missing content" exception of IStorageService.GetAsync, as FileStorageService throws.
            throw new FileNotFoundException("The object '" + path + "' does not exist.", path, e);
        }

        return new OwnedReadStream(response.ResponseStream, response);
    }

    public Task<Uri> GetDownloadUriAsync(string path, CancellationToken cancellationToken = default)
    {
        var url = _client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = PrepareKey(path)
        });

        return Task.FromResult(new Uri(url));
    }

    public async Task<StoragePutResult> PutAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        if (content == null) throw new ArgumentNullException(nameof(content));

        // A temporary file, not a MemoryStream: a large package is never held in managed memory.
        using (var seekableContent = await StreamingStreams.SpoolAsync(content, cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await _client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = _bucket,
                    Key = PrepareKey(path),
                    InputStream = seekableContent,
                    ContentType = contentType,
                    // Make the upload idempotent: store the content only while the key is
                    // unused. S3 answers a taken key with "412 Precondition Failed".
                    IfNoneMatch = IfKeyIsUnused,
                    AutoResetStreamPosition = false,
                    AutoCloseStream = false
                }, cancellationToken);

                return StoragePutResult.Success;
            }
            catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                // The key is taken. Storing the same content twice is not a failure, so
                // this is only a conflict when the stored content differs.
                using (var targetStream = await GetAsync(path, cancellationToken))
                {
                    seekableContent.Seek(0, SeekOrigin.Begin);

                    return seekableContent.Matches(targetStream)
                        ? StoragePutResult.AlreadyExists
                        : StoragePutResult.Conflict;
                }
            }
        }
    }

    /// <summary>
    /// Every key under a prefix, paged until S3 says there is no more. The sweep of
    /// abandoned staged uploads is the only caller, and without this a bucket would keep
    /// them forever.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListAsync(string pathPrefix, CancellationToken cancellationToken = default)
    {
        // A prefix is a path segment, not a string prefix: "staging" must not also match
        // "staging-archive/...".
        var prefix = PrepareKey(pathPrefix.TrimEnd('/') + Separator);
        var paths = new List<string>();
        string? continuation = null;

        do
        {
            var response = await _client.ListObjectsV2Async(
                new ListObjectsV2Request
                {
                    BucketName = _bucket,
                    Prefix = prefix,
                    ContinuationToken = continuation,
                },
                cancellationToken);

            foreach (var item in response.S3Objects)
            {
                // Reported as the paths IStorageService takes, so the configured bucket
                // prefix is removed again.
                paths.Add(string.IsNullOrEmpty(_prefix) ? item.Key : item.Key.Substring(_prefix.Length));
            }

            continuation = response.IsTruncated == true ? response.NextContinuationToken : null;
        }
        while (continuation != null);

        return paths;
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        await _client.DeleteObjectAsync(_bucket, PrepareKey(path), cancellationToken);
    }
}