using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;

namespace BaGet.Azure;

/// <summary>
/// Streaming uploads on block blobs (<see cref="IStreamingStorageService"/>). Chunks are staged as uncommitted blocks of a
/// temporary blob under <c>.uploads/</c> (a block is not limited below, so no tail is kept); at commit the block list is
/// committed there and the blob is copied server side to its final path with an if-not-exists condition. The upload's content
/// type lives in a small <c>.meta</c> blob, whose existence is also what makes an upload with no block yet exist.
/// Memory per request: one block (<see cref="BlockSize"/>). A blob holds at most 50 000 blocks, so the largest upload is
/// 50 000 chunks of up to <see cref="BlockSize"/> (400 GB when chunks are large, less when they are small).
/// </summary>
public partial class BlobStorageService
{
    /// <summary>The size a chunk is cut into when staged.</summary>
    internal const int BlockSize = 8 * 1024 * 1024;

    private const string GenericContentType = "application/octet-stream";

    public async Task<string> BeginUploadAsync(string contentType, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var meta = MetaBlob(id);
        meta.Properties.ContentType = "text/plain";
        await meta.UploadTextAsync(string.IsNullOrEmpty(contentType) ? GenericContentType : contentType, cancellationToken).ConfigureAwait(false);
        return id;
    }

    public async Task<long> AppendAsync(string uploadId, long offset, Stream chunk, CancellationToken cancellationToken = default)
    {
        if (chunk == null) throw new ArgumentNullException(nameof(chunk));

        await EnsureExistsAsync(uploadId, cancellationToken).ConfigureAwait(false);
        var blocks = await ListBlocksAsync(uploadId, cancellationToken).ConfigureAwait(false);
        var length = blocks.Sum(b => b.Length);
        if (length != offset) throw new StorageUploadOffsetException(length, offset);

        var temp = TempBlob(uploadId);
        var index = blocks.Count;
        var tracked = UploadDigestTracker.Shared.Track(uploadId, offset, chunk);
        try
        {
            var stored = length;

            async Task StageAsync(ReadOnlyMemory<byte> block, CancellationToken ct)
            {
                using var body = StreamingStreams.AsStream(block);
                await temp.PutBlockAsync(BlockId(index++), body, null, ct).ConfigureAwait(false);
                stored += block.Length;
            }

            var rest = await StreamPartitioner.SplitAsync(tracked, ReadOnlyMemory<byte>.Empty, BlockSize, StageAsync, cancellationToken).ConfigureAwait(false);
            if (rest.Length > 0) await StageAsync(rest, cancellationToken).ConfigureAwait(false);

            tracked.Complete(stored);
            return stored;
        }
        catch
        {
            tracked.Fail();
            throw;
        }
    }

    public async Task<long?> GetUploadLengthAsync(string uploadId, CancellationToken cancellationToken = default)
    {
        if (!IsUploadId(uploadId) || !await MetaBlob(uploadId).ExistsAsync(cancellationToken).ConfigureAwait(false)) return null;

        return (await ListBlocksAsync(uploadId, cancellationToken).ConfigureAwait(false)).Sum(b => b.Length);
    }

    public async Task<StorageCommitResult> CommitAsync(string uploadId, string path, string? expectedSha256, CancellationToken cancellationToken = default)
    {
        await EnsureExistsAsync(uploadId, cancellationToken).ConfigureAwait(false);

        var blocks = await ListBlocksAsync(uploadId, cancellationToken).ConfigureAwait(false);
        var length = blocks.Sum(b => b.Length);
        var temp = TempBlob(uploadId);
        var sha = UploadDigestTracker.Shared.TryComplete(uploadId, length);

        try
        {
            if (sha != null && expectedSha256 != null && !string.Equals(expectedSha256, sha, StringComparison.OrdinalIgnoreCase))
            {
                throw new StorageDigestMismatchException(expectedSha256, sha);
            }

            temp.Properties.ContentType = (await MetaBlob(uploadId).DownloadTextAsync(cancellationToken).ConfigureAwait(false)).Trim();
            await temp.PutBlockListAsync(blocks.Select(b => b.Name).ToList(), cancellationToken).ConfigureAwait(false);

            // Unknown when this node did not see every chunk: read the assembled blob once, streaming.
            if (sha == null)
            {
                using var read = await temp.OpenReadAsync(cancellationToken).ConfigureAwait(false);
                sha = await StreamingStorage.Sha256HexAsync(read, cancellationToken).ConfigureAwait(false);
            }

            StreamingStorage.VerifyDigest(expectedSha256, sha);

            var target = _container.GetBlockBlobReference(path);
            try
            {
                await target.StartCopyAsync(
                    temp.Uri,
                    sourceAccessCondition: null,
                    destAccessCondition: AccessCondition.GenerateIfNotExistsCondition(),
                    options: null,
                    operationContext: null,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                await WaitForCopyAsync(target, cancellationToken).ConfigureAwait(false);
                return new StorageCommitResult(StoragePutResult.Success, length, sha);
            }
            catch (StorageException e) when (e.IsAlreadyExistsException())
            {
                string existing;
                using (var stored = await target.OpenReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    existing = await StreamingStorage.Sha256HexAsync(stored, cancellationToken).ConfigureAwait(false);
                }

                return new StorageCommitResult(
                    string.Equals(existing, sha, StringComparison.Ordinal) ? StoragePutResult.AlreadyExists : StoragePutResult.Conflict,
                    length,
                    sha);
            }
        }
        finally
        {
            await DeleteUploadAsync(uploadId).ConfigureAwait(false);
        }
    }

    public Task AbortAsync(string uploadId, CancellationToken cancellationToken = default)
        => DeleteUploadAsync(uploadId);

    public async Task<StorageBlobInfo?> GetInfoAsync(string path, CancellationToken cancellationToken = default)
    {
        var blob = _container.GetBlockBlobReference(path);
        try
        {
            await blob.FetchAttributesAsync(cancellationToken).ConfigureAwait(false);
            return new StorageBlobInfo(blob.Properties.Length, blob.Properties.ContentType);
        }
        catch (StorageException e) when (e.RequestInformation?.HttpStatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Stream?> GetRangeAsync(string path, long offset, long? length, CancellationToken cancellationToken = default)
    {
        var blob = _container.GetBlockBlobReference(path);
        Stream stream;
        try
        {
            stream = await blob.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (StorageException e) when (e.RequestInformation?.HttpStatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }

        try
        {
            var count = StreamingStorage.ResolveRange(stream.Length, offset, length);
            stream.Seek(offset, SeekOrigin.Begin);
            return new RangeReadStream(stream, count);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static bool IsUploadId(string uploadId) => Guid.TryParseExact(uploadId, "N", out _);

    private CloudBlockBlob TempBlob(string uploadId) => _container.GetBlockBlobReference(StreamingStorage.UploadsFolder + "/" + uploadId);

    private CloudBlockBlob MetaBlob(string uploadId) => _container.GetBlockBlobReference(StreamingStorage.UploadsFolder + "/" + uploadId + ".meta");

    // Fixed-length ids are required: every block of a blob has an id of the same length.
    internal static string BlockId(int index) => Convert.ToBase64String(Encoding.ASCII.GetBytes(index.ToString("D8")));

    // Base64 text does not sort like what it encodes (digits come after letters in its alphabet), so blocks are ordered by the
    // index inside the id, never by the id text: that put a 5 GB blob (more than ~320 blocks) together out of order.
    internal static int BlockIndex(string blockId) => int.Parse(Encoding.ASCII.GetString(Convert.FromBase64String(blockId)), System.Globalization.CultureInfo.InvariantCulture);

    internal static IEnumerable<string> InUploadOrder(IEnumerable<string> blockIds) => blockIds.OrderBy(BlockIndex);

    private async Task EnsureExistsAsync(string uploadId, CancellationToken cancellationToken)
    {
        if (!IsUploadId(uploadId) || !await MetaBlob(uploadId).ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new StorageUploadNotFoundException(uploadId);
        }
    }

    private async Task<List<ListBlockItem>> ListBlocksAsync(string uploadId, CancellationToken cancellationToken)
    {
        try
        {
            var blocks = await TempBlob(uploadId).DownloadBlockListAsync(BlockListingFilter.Uncommitted, null, null, null, cancellationToken).ConfigureAwait(false);
            return blocks.OrderBy(b => BlockIndex(b.Name)).ToList();
        }
        catch (StorageException e) when (e.RequestInformation?.HttpStatusCode == (int)HttpStatusCode.NotFound)
        {
            // Nothing staged yet: the blob itself does not exist.
            return new List<ListBlockItem>();
        }
    }

    private static async Task WaitForCopyAsync(CloudBlockBlob target, CancellationToken cancellationToken)
    {
        while (true)
        {
            await target.FetchAttributesAsync(cancellationToken).ConfigureAwait(false);
            var state = target.CopyState;
            if (state == null || state.Status == CopyStatus.Success) return;
            if (state.Status != CopyStatus.Pending) throw new IOException($"The copy of the upload ended as {state.Status}.");

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DeleteUploadAsync(string uploadId)
    {
        UploadDigestTracker.Shared.Forget(uploadId);
        if (!IsUploadId(uploadId)) return;

        await TempBlob(uploadId).DeleteIfExistsAsync().ConfigureAwait(false);
        await MetaBlob(uploadId).DeleteIfExistsAsync().ConfigureAwait(false);
    }
}
