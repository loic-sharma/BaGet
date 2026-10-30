using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using BaGet.Core;

namespace BaGet.Aws;

/// <summary>
/// Streaming uploads on S3 multipart uploads (<see cref="IStreamingStorageService"/>). The final key is only known at commit, so
/// the parts are assembled into a temporary object under <c>.uploads/</c> and then copied (server side, in ranges, so any size)
/// into the final key through a multipart copy completed with <c>If-None-Match: *</c>, which makes the commit atomic and safe
/// against a concurrent identical commit.
/// </summary>
/// <remarks>
/// S3 wants every part but the last to be at least 5 MiB while a chunk may be any size, so bytes short of a part are kept in a
/// small <c>.tail</c> object (at most one part) that the next chunk is prepended with; the state of an upload is therefore all in
/// S3 (listed parts plus the tail) and any node can take the next chunk. Memory per request: one part (<see cref="PartSize"/>).
/// A blob that never reaches one part is stored with a single put and the generic content type.
/// </remarks>
public partial class S3StorageService
{
    /// <summary>The size of a part: above S3's 5 MiB minimum, and 10 000 parts reach 160 GB.</summary>
    internal const int PartSize = 16 * 1024 * 1024;

    private const string GenericContentType = "application/octet-stream";

    // A copied range is one part of the final multipart copy (limit 5 GiB each).
    private const long CopyRangeSize = 512L * 1024 * 1024;

    public async Task<string> BeginUploadAsync(string contentType, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var response = await _client.InitiateMultipartUploadAsync(
            new InitiateMultipartUploadRequest
            {
                BucketName = _bucket,
                Key = TempKey(id),
                ContentType = string.IsNullOrEmpty(contentType) ? GenericContentType : contentType,
            },
            cancellationToken).ConfigureAwait(false);

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(id + "|" + response.UploadId))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public async Task<long> AppendAsync(string uploadId, long offset, Stream chunk, CancellationToken cancellationToken = default)
    {
        if (chunk == null) throw new ArgumentNullException(nameof(chunk));

        var (id, s3Id) = Decode(uploadId);
        var state = await ReadStateAsync(id, s3Id, uploadId, cancellationToken).ConfigureAwait(false);
        if (state.Length != offset) throw new StorageUploadOffsetException(state.Length, offset);

        var tracked = UploadDigestTracker.Shared.Track(uploadId, offset, chunk);
        try
        {
            var partNumber = state.LastPartNumber;
            var stored = state.PartsLength;

            var tail = await StreamPartitioner.SplitAsync(
                tracked,
                state.Tail,
                PartSize,
                async (part, ct) =>
                {
                    partNumber++;
                    using var body = StreamingStreams.AsStream(part);
                    await _client.UploadPartAsync(
                        new UploadPartRequest
                        {
                            BucketName = _bucket,
                            Key = TempKey(id),
                            UploadId = s3Id,
                            PartNumber = partNumber,
                            InputStream = body,
                            PartSize = part.Length,
                        },
                        ct).ConfigureAwait(false);
                    stored += part.Length;
                },
                cancellationToken).ConfigureAwait(false);

            if (tail.Length > 0)
            {
                using var body = new MemoryStream(tail, writable: false);
                await _client.PutObjectAsync(
                    new PutObjectRequest { BucketName = _bucket, Key = TailKey(id), InputStream = body, AutoCloseStream = false },
                    cancellationToken).ConfigureAwait(false);
            }
            else if (state.Tail.Length > 0)
            {
                await _client.DeleteObjectAsync(_bucket, TailKey(id), cancellationToken).ConfigureAwait(false);
            }

            var length = stored + tail.Length;
            tracked.Complete(length);
            return length;
        }
        catch
        {
            tracked.Fail();
            throw;
        }
    }

    public async Task<long?> GetUploadLengthAsync(string uploadId, CancellationToken cancellationToken = default)
    {
        try
        {
            var (id, s3Id) = Decode(uploadId);
            return (await ReadStateAsync(id, s3Id, uploadId, cancellationToken).ConfigureAwait(false)).Length;
        }
        catch (StorageUploadNotFoundException)
        {
            return null;
        }
    }

    public async Task<StorageCommitResult> CommitAsync(string uploadId, string path, string? expectedSha256, CancellationToken cancellationToken = default)
    {
        var (id, s3Id) = Decode(uploadId);
        var state = await ReadStateAsync(id, s3Id, uploadId, cancellationToken).ConfigureAwait(false);
        var finalKey = PrepareKey(path);
        var tempKey = TempKey(id);

        var sha = UploadDigestTracker.Shared.TryComplete(uploadId, state.Length);

        // Nothing but a tail (a small blob, or an empty one): no multipart is needed, so it is put in one request.
        if (state.LastPartNumber == 0)
        {
            sha ??= StreamingStorage.Sha256Hex(state.Tail);
            if (expectedSha256 != null && !string.Equals(expectedSha256, sha, StringComparison.OrdinalIgnoreCase))
            {
                await DiscardAsync(id, s3Id, cancellationToken).ConfigureAwait(false);
                throw new StorageDigestMismatchException(expectedSha256, sha);
            }

            var result = await PutIfAbsentAsync(finalKey, state.Tail, sha, cancellationToken).ConfigureAwait(false);
            await DiscardAsync(id, s3Id, cancellationToken).ConfigureAwait(false);
            return new StorageCommitResult(result, state.Length, sha);
        }

        // Digest known: refuse a wrong blob before assembling it.
        if (sha != null && expectedSha256 != null && !string.Equals(expectedSha256, sha, StringComparison.OrdinalIgnoreCase))
        {
            await DiscardAsync(id, s3Id, cancellationToken).ConfigureAwait(false);
            throw new StorageDigestMismatchException(expectedSha256, sha);
        }

        var parts = await ListPartsAsync(tempKey, s3Id, cancellationToken).ConfigureAwait(false);
        if (state.Tail.Length > 0)
        {
            using var body = new MemoryStream(state.Tail, writable: false);
            var response = await _client.UploadPartAsync(
                new UploadPartRequest
                {
                    BucketName = _bucket,
                    Key = tempKey,
                    UploadId = s3Id,
                    PartNumber = state.LastPartNumber + 1,
                    InputStream = body,
                    PartSize = state.Tail.Length,
                },
                cancellationToken).ConfigureAwait(false);
            parts.Add(new PartETag(state.LastPartNumber + 1, response.ETag));
        }

        await _client.CompleteMultipartUploadAsync(
            new CompleteMultipartUploadRequest { BucketName = _bucket, Key = tempKey, UploadId = s3Id, PartETags = parts },
            cancellationToken).ConfigureAwait(false);

        try
        {
            // The digest is unknown when this node did not see every chunk: read the assembled object once, streaming.
            sha ??= await ReadSha256Async(tempKey, cancellationToken).ConfigureAwait(false);

            StreamingStorage.VerifyDigest(expectedSha256, sha);

            var meta = await _client.GetObjectMetadataAsync(_bucket, tempKey, cancellationToken).ConfigureAwait(false);
            var put = await CopyIfAbsentAsync(tempKey, finalKey, state.Length, meta.Headers.ContentType, sha, cancellationToken).ConfigureAwait(false);
            return new StorageCommitResult(put, state.Length, sha);
        }
        finally
        {
            await _client.DeleteObjectAsync(_bucket, tempKey, CancellationToken.None).ConfigureAwait(false);
            await _client.DeleteObjectAsync(_bucket, TailKey(id), CancellationToken.None).ConfigureAwait(false);
            UploadDigestTracker.Shared.Forget(uploadId);
        }
    }

    public async Task AbortAsync(string uploadId, CancellationToken cancellationToken = default)
    {
        UploadDigestTracker.Shared.Forget(uploadId);

        string id, s3Id;
        try
        {
            (id, s3Id) = Decode(uploadId);
        }
        catch (StorageUploadNotFoundException)
        {
            return;
        }

        await DiscardAsync(id, s3Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StorageBlobInfo?> GetInfoAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var meta = await _client.GetObjectMetadataAsync(_bucket, PrepareKey(path), cancellationToken).ConfigureAwait(false);
            return new StorageBlobInfo(meta.ContentLength, meta.Headers.ContentType);
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Stream?> GetRangeAsync(string path, long offset, long? length, CancellationToken cancellationToken = default)
    {
        var info = await GetInfoAsync(path, cancellationToken).ConfigureAwait(false);
        if (info == null) return null;

        var count = StreamingStorage.ResolveRange(info.Length, offset, length);
        if (count == 0) return Stream.Null;

        // Bounded range requests, each re-requested from the exact byte if its connection drops: one GET of many GB must not be the unit of failure.
        return new WindowedReadStream(
            async (from, size, ct) =>
            {
                var response = await _client.GetObjectAsync(
                    new GetObjectRequest { BucketName = _bucket, Key = PrepareKey(path), ByteRange = new ByteRange(from, from + size - 1) },
                    ct).ConfigureAwait(false);
                return new OwnedReadStream(response.ResponseStream, response);
            },
            offset,
            count);
    }

    private string TempKey(string id) => PrepareKey(StreamingStorage.UploadsFolder + Separator + id);

    private string TailKey(string id) => PrepareKey(StreamingStorage.UploadsFolder + Separator + id + ".tail");

    private static (string Id, string S3Id) Decode(string uploadId)
    {
        try
        {
            var padded = uploadId.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var bar = text.IndexOf('|');
            if (bar == 32 && Guid.TryParseExact(text.Substring(0, bar), "N", out _) && bar < text.Length - 1)
            {
                return (text.Substring(0, bar), text.Substring(bar + 1));
            }
        }
        catch (FormatException)
        {
        }

        throw new StorageUploadNotFoundException(uploadId);
    }

    private sealed record UploadState(int LastPartNumber, long PartsLength, byte[] Tail)
    {
        public long Length => PartsLength + Tail.Length;
    }

    private async Task<UploadState> ReadStateAsync(string id, string s3Id, string uploadId, CancellationToken cancellationToken)
    {
        var last = 0;
        long size = 0;
        try
        {
            foreach (var part in await ListPartDetailsAsync(TempKey(id), s3Id, cancellationToken).ConfigureAwait(false))
            {
                last = Math.Max(last, part.PartNumber.GetValueOrDefault());
                size += part.Size.GetValueOrDefault();
            }
        }
        catch (AmazonS3Exception e) when (e.ErrorCode == "NoSuchUpload" || e.StatusCode == HttpStatusCode.NotFound)
        {
            throw new StorageUploadNotFoundException(uploadId);
        }

        var tail = Array.Empty<byte>();
        try
        {
            using var response = await _client.GetObjectAsync(_bucket, TailKey(id), cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
            tail = buffer.ToArray();
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
        }

        return new UploadState(last, size, tail);
    }

    private async Task<List<PartDetail>> ListPartDetailsAsync(string key, string s3Id, CancellationToken cancellationToken)
    {
        var result = new List<PartDetail>();
        string? marker = null;
        while (true)
        {
            var page = await _client.ListPartsAsync(
                new ListPartsRequest { BucketName = _bucket, Key = key, UploadId = s3Id, PartNumberMarker = marker },
                cancellationToken).ConfigureAwait(false);

            if (page.Parts != null) result.AddRange(page.Parts);

            if (page.IsTruncated != true) break;
            marker = page.NextPartNumberMarker?.ToString();
        }

        return result;
    }

    private async Task<List<PartETag>> ListPartsAsync(string key, string s3Id, CancellationToken cancellationToken)
    {
        var parts = await ListPartDetailsAsync(key, s3Id, cancellationToken).ConfigureAwait(false);
        return parts
            .Select(p => new PartETag(p.PartNumber.GetValueOrDefault(), p.ETag))
            .OrderBy(p => p.PartNumber)
            .ToList();
    }

    private async Task<string> ReadSha256Async(string key, CancellationToken cancellationToken)
    {
        using var stored = await _client.GetObjectAsync(_bucket, key, cancellationToken).ConfigureAwait(false);
        return await StreamingStorage.Sha256HexAsync(stored.ResponseStream, cancellationToken).ConfigureAwait(false);
    }

    private async Task DiscardAsync(string id, string s3Id, CancellationToken cancellationToken)
    {
        try
        {
            await _client.AbortMultipartUploadAsync(
                new AbortMultipartUploadRequest { BucketName = _bucket, Key = TempKey(id), UploadId = s3Id },
                cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception e) when (e.ErrorCode == "NoSuchUpload" || e.StatusCode == HttpStatusCode.NotFound)
        {
        }

        await _client.DeleteObjectAsync(_bucket, TailKey(id), cancellationToken).ConfigureAwait(false);
    }

    private async Task<StoragePutResult> PutIfAbsentAsync(string key, byte[] content, string sha, CancellationToken cancellationToken)
    {
        try
        {
            using var body = new MemoryStream(content, writable: false);
            await _client.PutObjectAsync(
                new PutObjectRequest
                {
                    BucketName = _bucket,
                    Key = key,
                    InputStream = body,
                    ContentType = GenericContentType,
                    IfNoneMatch = IfKeyIsUnused,
                    AutoCloseStream = false,
                },
                cancellationToken).ConfigureAwait(false);
            return StoragePutResult.Success;
        }
        catch (AmazonS3Exception e) when (KeyIsTaken(e))
        {
            return await CompareWithStoredAsync(key, sha, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<StoragePutResult> CopyIfAbsentAsync(string sourceKey, string key, long size, string? contentType, string sha, CancellationToken cancellationToken)
    {
        // The conditional complete below is what makes this atomic on S3; the plain check first spares the copy in the usual case of
        // a blob that is already there, and covers S3-compatible stores that do not honour If-None-Match: * on a multipart complete.
        if (await ExistsAsync(key, cancellationToken).ConfigureAwait(false))
        {
            return await CompareWithStoredAsync(key, sha, cancellationToken).ConfigureAwait(false);
        }

        var start = await _client.InitiateMultipartUploadAsync(
            new InitiateMultipartUploadRequest { BucketName = _bucket, Key = key, ContentType = contentType },
            cancellationToken).ConfigureAwait(false);

        try
        {
            var etags = new List<PartETag>();
            var number = 0;
            for (long first = 0; first < size; first += CopyRangeSize)
            {
                number++;
                var copied = await _client.CopyPartAsync(
                    new CopyPartRequest
                    {
                        SourceBucket = _bucket,
                        SourceKey = sourceKey,
                        DestinationBucket = _bucket,
                        DestinationKey = key,
                        UploadId = start.UploadId,
                        PartNumber = number,
                        FirstByte = first,
                        LastByte = Math.Min(first + CopyRangeSize, size) - 1,
                    },
                    cancellationToken).ConfigureAwait(false);
                etags.Add(new PartETag(number, copied.ETag));
            }

            await _client.CompleteMultipartUploadAsync(
                new CompleteMultipartUploadRequest { BucketName = _bucket, Key = key, UploadId = start.UploadId, PartETags = etags, IfNoneMatch = IfKeyIsUnused },
                cancellationToken).ConfigureAwait(false);
            return StoragePutResult.Success;
        }
        catch (AmazonS3Exception e) when (KeyIsTaken(e))
        {
            // Compared before the abort: the stored blob is what the answer is about.
            var result = await CompareWithStoredAsync(key, sha, cancellationToken).ConfigureAwait(false);
            await AbortQuietlyAsync(key, start.UploadId).ConfigureAwait(false);
            return result;
        }
        catch
        {
            await AbortQuietlyAsync(key, start.UploadId).ConfigureAwait(false);
            throw;
        }
    }

    // S3 answers a taken key with 412 Precondition Failed; some S3-compatible stores answer 304 Not Modified to the same header.
    private static bool KeyIsTaken(AmazonS3Exception e)
        => e.StatusCode == HttpStatusCode.PreconditionFailed || e.StatusCode == HttpStatusCode.NotModified;

    private async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await _client.GetObjectMetadataAsync(_bucket, key, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private async Task AbortQuietlyAsync(string key, string uploadId)
    {
        try
        {
            await _client.AbortMultipartUploadAsync(
                new AbortMultipartUploadRequest { BucketName = _bucket, Key = key, UploadId = uploadId },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (AmazonS3Exception)
        {
        }
    }

    private async Task<StoragePutResult> CompareWithStoredAsync(string key, string sha, CancellationToken cancellationToken)
    {
        var existing = await ReadSha256Async(key, cancellationToken).ConfigureAwait(false);
        return string.Equals(existing, sha, StringComparison.Ordinal) ? StoragePutResult.AlreadyExists : StoragePutResult.Conflict;
    }
}
