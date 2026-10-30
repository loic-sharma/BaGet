using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aliyun.OSS;
using BaGet.Core;
using OssPart = global::Aliyun.OSS.Part;
using OssException = global::Aliyun.OSS.Common.OssException;

namespace BaGet.Aliyun;

/// <summary>
/// Streaming uploads on OSS multipart uploads (<see cref="IStreamingStorageService"/>), laid out exactly like the S3 provider:
/// parts are assembled into a temporary object under <c>.uploads/</c> and copied (in ranges) to the final key at commit; bytes
/// short of a part wait in a <c>.tail</c> object, so an upload's whole state is in OSS. Memory per request: one part
/// (<see cref="PartSize"/>).
/// </summary>
/// <remarks>
/// <b>Not verified against a live service:</b> OSS has no emulator, so this provider is covered by code review and the shared
/// contract only. The commit is also not atomic against a concurrent commit of the same final key: the SDK sends no
/// <c>x-oss-forbid-overwrite</c> (see <see cref="PutAsync"/>), so the key is checked first.
/// </remarks>
public partial class AliyunStorageService
{
    /// <summary>The size of a part: OSS wants at least 100 KB (except the last), allows 10 000 parts and 5 GB each.</summary>
    internal const int PartSize = 16 * 1024 * 1024;

    private const string GenericContentType = "application/octet-stream";

    // A copied range is one part of the final multipart copy.
    private const long CopyRangeSize = 512L * 1024 * 1024;

    public async Task<string> BeginUploadAsync(string contentType, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var result = await Run(() => _client.InitiateMultipartUpload(
            new InitiateMultipartUploadRequest(_bucket, TempKey(id))
            {
                ObjectMetadata = new ObjectMetadata { ContentType = string.IsNullOrEmpty(contentType) ? GenericContentType : contentType },
            }), cancellationToken).ConfigureAwait(false);

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(id + "|" + result.UploadId))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public async Task<long> AppendAsync(string uploadId, long offset, Stream chunk, CancellationToken cancellationToken = default)
    {
        if (chunk == null) throw new ArgumentNullException(nameof(chunk));

        var (id, ossId) = Decode(uploadId);
        var state = await ReadStateAsync(id, ossId, uploadId, cancellationToken).ConfigureAwait(false);
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
                    var number = partNumber;
                    var bytes = part;
                    await Run(() =>
                    {
                        using var body = StreamingStreams.AsStream(bytes);
                        return _client.UploadPart(new UploadPartRequest(_bucket, TempKey(id), ossId)
                        {
                            PartNumber = number,
                            PartSize = bytes.Length,
                            InputStream = body,
                        });
                    }, ct).ConfigureAwait(false);
                    stored += part.Length;
                },
                cancellationToken).ConfigureAwait(false);

            if (tail.Length > 0)
            {
                await Run(() =>
                {
                    using var body = new MemoryStream(tail, writable: false);
                    return _client.PutObject(_bucket, TailKey(id), body);
                }, cancellationToken).ConfigureAwait(false);
            }
            else if (state.Tail.Length > 0)
            {
                await Run(() => { _client.DeleteObject(_bucket, TailKey(id)); return 0; }, cancellationToken).ConfigureAwait(false);
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
            var (id, ossId) = Decode(uploadId);
            return (await ReadStateAsync(id, ossId, uploadId, cancellationToken).ConfigureAwait(false)).Length;
        }
        catch (StorageUploadNotFoundException)
        {
            return null;
        }
    }

    public async Task<StorageCommitResult> CommitAsync(string uploadId, string path, string? expectedSha256, CancellationToken cancellationToken = default)
    {
        var (id, ossId) = Decode(uploadId);
        var state = await ReadStateAsync(id, ossId, uploadId, cancellationToken).ConfigureAwait(false);
        var finalKey = PrepareKey(path);
        var tempKey = TempKey(id);
        var sha = UploadDigestTracker.Shared.TryComplete(uploadId, state.Length);

        if (sha != null && expectedSha256 != null && !string.Equals(expectedSha256, sha, StringComparison.OrdinalIgnoreCase))
        {
            await DiscardAsync(id, ossId, cancellationToken).ConfigureAwait(false);
            throw new StorageDigestMismatchException(expectedSha256, sha);
        }

        // Nothing but a tail: a single put, no multipart.
        if (state.LastPartNumber == 0)
        {
            sha ??= StreamingStorage.Sha256Hex(state.Tail);
            if (expectedSha256 != null && !string.Equals(expectedSha256, sha, StringComparison.OrdinalIgnoreCase))
            {
                await DiscardAsync(id, ossId, cancellationToken).ConfigureAwait(false);
                throw new StorageDigestMismatchException(expectedSha256, sha);
            }

            StoragePutResult put;
            if (await Run(() => _client.DoesObjectExist(_bucket, finalKey), cancellationToken).ConfigureAwait(false))
            {
                put = await CompareAsync(finalKey, sha, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await Run(() =>
                {
                    using var body = new MemoryStream(state.Tail, writable: false);
                    return _client.PutObject(_bucket, finalKey, body, new ObjectMetadata { ContentType = GenericContentType });
                }, cancellationToken).ConfigureAwait(false);
                put = StoragePutResult.Success;
            }

            await DiscardAsync(id, ossId, cancellationToken).ConfigureAwait(false);
            return new StorageCommitResult(put, state.Length, sha);
        }

        if (state.Tail.Length > 0)
        {
            await Run(() =>
            {
                using var body = new MemoryStream(state.Tail, writable: false);
                return _client.UploadPart(new UploadPartRequest(_bucket, tempKey, ossId)
                {
                    PartNumber = state.LastPartNumber + 1,
                    PartSize = state.Tail.Length,
                    InputStream = body,
                });
            }, cancellationToken).ConfigureAwait(false);
        }

        await Run(() =>
        {
            var complete = new CompleteMultipartUploadRequest(_bucket, tempKey, ossId);
            foreach (var part in ListParts(tempKey, ossId)) complete.PartETags.Add(new PartETag(part.PartNumber, part.ETag));
            return _client.CompleteMultipartUpload(complete);
        }, cancellationToken).ConfigureAwait(false);

        try
        {
            sha ??= await ReadSha256Async(tempKey, cancellationToken).ConfigureAwait(false);
            StreamingStorage.VerifyDigest(expectedSha256, sha);

            if (await Run(() => _client.DoesObjectExist(_bucket, finalKey), cancellationToken).ConfigureAwait(false))
            {
                return new StorageCommitResult(await CompareAsync(finalKey, sha, cancellationToken).ConfigureAwait(false), state.Length, sha);
            }

            await CopyAsync(tempKey, finalKey, state.Length, cancellationToken).ConfigureAwait(false);
            return new StorageCommitResult(StoragePutResult.Success, state.Length, sha);
        }
        finally
        {
            UploadDigestTracker.Shared.Forget(uploadId);
            await Run(() => { _client.DeleteObject(_bucket, tempKey); _client.DeleteObject(_bucket, TailKey(id)); return 0; }, CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async Task AbortAsync(string uploadId, CancellationToken cancellationToken = default)
    {
        UploadDigestTracker.Shared.Forget(uploadId);

        string id, ossId;
        try
        {
            (id, ossId) = Decode(uploadId);
        }
        catch (StorageUploadNotFoundException)
        {
            return;
        }

        await DiscardAsync(id, ossId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StorageBlobInfo?> GetInfoAsync(string path, CancellationToken cancellationToken = default)
    {
        var key = PrepareKey(path);
        return await Run(() =>
        {
            if (!_client.DoesObjectExist(_bucket, key)) return null;

            var meta = _client.GetObjectMetadata(_bucket, key);
            return new StorageBlobInfo(meta.ContentLength, meta.ContentType);
        }, cancellationToken).ConfigureAwait(false);
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
                var request = new GetObjectRequest(_bucket, PrepareKey(path));
                request.SetRange(from, from + size - 1);
                var result = await Run(() => _client.GetObject(request), ct).ConfigureAwait(false);
                return new OwnedReadStream(result.Content, result);
            },
            offset,
            count);
    }

    private string TempKey(string id) => PrepareKey(StreamingStorage.UploadsFolder + Separator + id);

    private string TailKey(string id) => PrepareKey(StreamingStorage.UploadsFolder + Separator + id + ".tail");

    private static Task<T> Run<T>(Func<T> work, CancellationToken cancellationToken)
        => Task.Run(work, cancellationToken);

    private static (string Id, string OssId) Decode(string uploadId)
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

    private List<OssPart> ListParts(string key, string ossId)
    {
        var parts = new List<OssPart>();
        int? marker = null;
        while (true)
        {
            var request = new ListPartsRequest(_bucket, key, ossId);
            if (marker != null) request.PartNumberMarker = marker;
            var page = _client.ListParts(request);
            parts.AddRange(page.Parts);

            if (!page.IsTruncated) break;
            marker = page.NextPartNumberMarker;
        }

        return parts.OrderBy(p => p.PartNumber).ToList();
    }

    private async Task<UploadState> ReadStateAsync(string id, string ossId, string uploadId, CancellationToken cancellationToken)
    {
        List<OssPart> parts;
        try
        {
            parts = await Run(() => ListParts(TempKey(id), ossId), cancellationToken).ConfigureAwait(false);
        }
        catch (OssException e) when (e.ErrorCode == "NoSuchUpload")
        {
            throw new StorageUploadNotFoundException(uploadId);
        }

        var tailKey = TailKey(id);
        var tail = await Run(() =>
        {
            if (!_client.DoesObjectExist(_bucket, tailKey)) return Array.Empty<byte>();

            using var result = _client.GetObject(_bucket, tailKey);
            using var buffer = new MemoryStream();
            result.Content.CopyTo(buffer);
            return buffer.ToArray();
        }, cancellationToken).ConfigureAwait(false);

        return new UploadState(parts.Count == 0 ? 0 : parts.Max(p => p.PartNumber), parts.Sum(p => p.Size), tail);
    }

    private async Task<string> ReadSha256Async(string key, CancellationToken cancellationToken)
    {
        using var result = await Run(() => _client.GetObject(_bucket, key), cancellationToken).ConfigureAwait(false);
        return await StreamingStorage.Sha256HexAsync(result.Content, cancellationToken).ConfigureAwait(false);
    }

    private async Task<StoragePutResult> CompareAsync(string key, string sha, CancellationToken cancellationToken)
    {
        var existing = await ReadSha256Async(key, cancellationToken).ConfigureAwait(false);
        return string.Equals(existing, sha, StringComparison.Ordinal) ? StoragePutResult.AlreadyExists : StoragePutResult.Conflict;
    }

    private Task CopyAsync(string sourceKey, string key, long size, CancellationToken cancellationToken)
        => Run(() =>
        {
            var start = _client.InitiateMultipartUpload(new InitiateMultipartUploadRequest(_bucket, key));
            try
            {
                var complete = new CompleteMultipartUploadRequest(_bucket, key, start.UploadId);
                var number = 0;
                for (long first = 0; first < size; first += CopyRangeSize)
                {
                    number++;
                    var copied = _client.UploadPartCopy(new UploadPartCopyRequest(_bucket, sourceKey, _bucket, key, start.UploadId)
                    {
                        PartNumber = number,
                        BeginIndex = first,
                        PartSize = Math.Min(CopyRangeSize, size - first),
                    });
                    complete.PartETags.Add(copied.PartETag);
                }

                if (number == 0)
                {
                    _client.AbortMultipartUpload(new AbortMultipartUploadRequest(_bucket, key, start.UploadId));
                    using var empty = new MemoryStream();
                    _client.PutObject(_bucket, key, empty);
                    return 0;
                }

                _client.CompleteMultipartUpload(complete);
                return 0;
            }
            catch
            {
                try
                {
                    _client.AbortMultipartUpload(new AbortMultipartUploadRequest(_bucket, key, start.UploadId));
                }
                catch (OssException)
                {
                }

                throw;
            }
        }, cancellationToken);

    private async Task DiscardAsync(string id, string ossId, CancellationToken cancellationToken)
    {
        await Run(() =>
        {
            try
            {
                _client.AbortMultipartUpload(new AbortMultipartUploadRequest(_bucket, TempKey(id), ossId));
            }
            catch (OssException e) when (e.ErrorCode == "NoSuchUpload")
            {
            }

            _client.DeleteObject(_bucket, TailKey(id));
            return 0;
        }, cancellationToken).ConfigureAwait(false);
    }
}
