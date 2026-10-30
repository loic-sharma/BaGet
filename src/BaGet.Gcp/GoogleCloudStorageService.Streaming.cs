using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Google;
using Google.Cloud.Storage.V1;

namespace BaGet.Gcp;

/// <summary>
/// Streaming uploads on Cloud Storage resumable upload sessions (<see cref="IStreamingStorageService"/>). A session writes to a
/// temporary object under <c>.uploads/</c> (the final name is only known at commit); at commit the session is finalised, the
/// object is verified and copied server side to its final name with <c>ifGenerationMatch=0</c>.
/// </summary>
/// <remarks>
/// <para>The sessions are driven over HTTP directly (the client library resumes from a seekable whole-content stream, which a
/// chunked upload does not have). GCS wants every chunk but the last to be a multiple of 256 KiB, so bytes short of a chunk are
/// kept in a small <c>.tail</c> object (at most <see cref="ChunkSize"/>) that the next chunk is prepended with; the state of an
/// upload is the session's own persisted size plus that tail, so any node can take the next chunk. Memory per request: one chunk.</para>
/// <para>The upload id is opaque: the upload's name and the session URI.</para>
/// </remarks>
public partial class GoogleCloudStorageService
{
    /// <summary>The chunk size: a multiple of 256 KiB, as resumable uploads require.</summary>
    internal const int ChunkSize = 8 * 1024 * 1024;

    private const string GenericContentType = "application/octet-stream";

    public async Task<string> BeginUploadAsync(string contentType, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString("N");
        using var storage = await CreateClientAsync().ConfigureAwait(false);

        var url = UploadBase(storage) + "b/" + Uri.EscapeDataString(_bucketName!) + "/o?uploadType=resumable&name=" + Uri.EscapeDataString(TempName(id));
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent("{\"name\":\"" + TempName(id) + "\"}", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("X-Upload-Content-Type", string.IsNullOrEmpty(contentType) ? GenericContentType : contentType);

        using var response = await storage.Service.HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var session = response.Headers.Location ?? throw new IOException("The upload session was created without a location.");

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(id + "|" + session.AbsoluteUri))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public async Task<long> AppendAsync(string uploadId, long offset, Stream chunk, CancellationToken cancellationToken = default)
    {
        if (chunk == null) throw new ArgumentNullException(nameof(chunk));

        var (id, session) = Decode(uploadId);
        using var storage = await CreateClientAsync().ConfigureAwait(false);
        var state = await ReadStateAsync(storage, id, session, uploadId, cancellationToken).ConfigureAwait(false);
        if (state.Length != offset) throw new StorageUploadOffsetException(state.Length, offset);

        var tracked = UploadDigestTracker.Shared.Track(uploadId, offset, chunk);
        try
        {
            var persisted = state.Persisted;

            var tail = await StreamPartitioner.SplitAsync(
                tracked,
                state.Tail,
                ChunkSize,
                async (part, ct) =>
                {
                    await PutRangeAsync(storage, session, persisted, part, total: null, ct).ConfigureAwait(false);
                    persisted += part.Length;
                },
                cancellationToken).ConfigureAwait(false);

            if (tail.Length > 0)
            {
                await PutTailAsync(storage, TailName(id), tail, cancellationToken).ConfigureAwait(false);
            }
            else if (state.Tail.Length > 0)
            {
                await DeleteQuietlyAsync(storage, TailName(id), cancellationToken).ConfigureAwait(false);
            }

            var length = persisted + tail.Length;
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
            var (id, session) = Decode(uploadId);
            using var storage = await CreateClientAsync().ConfigureAwait(false);
            return (await ReadStateAsync(storage, id, session, uploadId, cancellationToken).ConfigureAwait(false)).Length;
        }
        catch (StorageUploadNotFoundException)
        {
            return null;
        }
    }

    public async Task<StorageCommitResult> CommitAsync(string uploadId, string path, string? expectedSha256, CancellationToken cancellationToken = default)
    {
        var (id, session) = Decode(uploadId);
        using var storage = await CreateClientAsync().ConfigureAwait(false);
        var state = await ReadStateAsync(storage, id, session, uploadId, cancellationToken).ConfigureAwait(false);
        var sha = UploadDigestTracker.Shared.TryComplete(uploadId, state.Length);
        var temp = TempName(id);
        var finalName = CoercePath(path);

        try
        {
            if (sha != null && expectedSha256 != null && !string.Equals(expectedSha256, sha, StringComparison.OrdinalIgnoreCase))
            {
                await CancelSessionAsync(storage, session).ConfigureAwait(false);
                throw new StorageDigestMismatchException(expectedSha256, sha);
            }

            // Finalise the session with the tail (or with an empty body when everything is persisted).
            await PutRangeAsync(storage, session, state.Persisted, state.Tail, total: state.Length, cancellationToken).ConfigureAwait(false);

            if (sha == null)
            {
                using var read = await GetRangeAsync(temp, 0, null, cancellationToken).ConfigureAwait(false)
                    ?? throw new StorageUploadNotFoundException(uploadId);
                sha = await StreamingStorage.Sha256HexAsync(read, cancellationToken).ConfigureAwait(false);
            }

            StreamingStorage.VerifyDigest(expectedSha256, sha);

            try
            {
                await storage.CopyObjectAsync(
                    _bucketName,
                    temp,
                    _bucketName,
                    finalName,
                    new CopyObjectOptions { IfGenerationMatch = 0 },
                    cancellationToken).ConfigureAwait(false);
                return new StorageCommitResult(StoragePutResult.Success, state.Length, sha);
            }
            catch (GoogleApiException e) when (e.HttpStatusCode == HttpStatusCode.PreconditionFailed)
            {
                string existing;
                using (var stored = await GetRangeAsync(finalName, 0, null, cancellationToken).ConfigureAwait(false) ?? Stream.Null)
                {
                    existing = await StreamingStorage.Sha256HexAsync(stored, cancellationToken).ConfigureAwait(false);
                }

                return new StorageCommitResult(
                    string.Equals(existing, sha, StringComparison.Ordinal) ? StoragePutResult.AlreadyExists : StoragePutResult.Conflict,
                    state.Length,
                    sha);
            }
        }
        finally
        {
            UploadDigestTracker.Shared.Forget(uploadId);
            await DeleteQuietlyAsync(storage, temp, CancellationToken.None).ConfigureAwait(false);
            await DeleteQuietlyAsync(storage, TailName(id), CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async Task AbortAsync(string uploadId, CancellationToken cancellationToken = default)
    {
        UploadDigestTracker.Shared.Forget(uploadId);

        string id;
        Uri session;
        try
        {
            (id, session) = Decode(uploadId);
        }
        catch (StorageUploadNotFoundException)
        {
            return;
        }

        using var storage = await CreateClientAsync().ConfigureAwait(false);
        await CancelSessionAsync(storage, session).ConfigureAwait(false);
        await DeleteQuietlyAsync(storage, TempName(id), cancellationToken).ConfigureAwait(false);
        await DeleteQuietlyAsync(storage, TailName(id), cancellationToken).ConfigureAwait(false);
    }

    public async Task<StorageBlobInfo?> GetInfoAsync(string path, CancellationToken cancellationToken = default)
    {
        using var storage = await CreateClientAsync().ConfigureAwait(false);
        try
        {
            var obj = await storage.GetObjectAsync(_bucketName, CoercePath(path), cancellationToken: cancellationToken).ConfigureAwait(false);
            return new StorageBlobInfo(checked((long)(obj.Size ?? 0)), obj.ContentType);
        }
        catch (GoogleApiException e) when (e.HttpStatusCode == HttpStatusCode.NotFound)
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

        var storage = await CreateClientAsync().ConfigureAwait(false);
        var url = storage.Service.BaseUri + "b/" + Uri.EscapeDataString(_bucketName!) + "/o/" + Uri.EscapeDataString(CoercePath(path)) + "?alt=media";

        // Bounded range requests, each re-requested from the exact byte if its connection drops: one GET of many GB must not be the unit of failure.
        var windows = new WindowedReadStream(
            async (from, size, ct) =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new RangeHeaderValue(from, from + size - 1);

                var response = await storage.Service.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                try
                {
                    response.EnsureSuccessStatusCode();
                    var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

                    // Servers that ignore Range answer 200 with the whole object: skip to the offset and stop at the count.
                    if (response.StatusCode == HttpStatusCode.OK && from > 0)
                    {
                        var skip = new byte[81920];
                        for (var left = from; left > 0;)
                        {
                            var read = await body.ReadAsync(skip.AsMemory(0, (int)Math.Min(skip.Length, left)), ct).ConfigureAwait(false);
                            if (read == 0) break;
                            left -= read;
                        }
                    }

                    return new OwnedReadStream(new RangeReadStream(body, size), response);
                }
                catch
                {
                    response.Dispose();
                    throw;
                }
            },
            offset,
            count);

        return new OwnedReadStream(windows, storage);
    }

    private string TempName(string id) => StreamingStorage.UploadsFolder + "/" + id;

    private string TailName(string id) => StreamingStorage.UploadsFolder + "/" + id + ".tail";

    private static string UploadBase(StorageClient storage)
        => storage.Service.BaseUri.Replace("/storage/v1/", "/upload/storage/v1/");

    private static (string Id, Uri Session) Decode(string uploadId)
    {
        try
        {
            var padded = uploadId.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var bar = text.IndexOf('|');
            if (bar == 32 && Guid.TryParseExact(text.Substring(0, bar), "N", out _)
                && Uri.TryCreate(text.Substring(bar + 1), UriKind.Absolute, out var session))
            {
                return (text.Substring(0, bar), session);
            }
        }
        catch (FormatException)
        {
        }

        throw new StorageUploadNotFoundException(uploadId);
    }

    private sealed record UploadState(long Persisted, byte[] Tail)
    {
        public long Length => Persisted + Tail.Length;
    }

    private async Task<UploadState> ReadStateAsync(StorageClient storage, string id, Uri session, string uploadId, CancellationToken cancellationToken)
    {
        // "bytes */*" asks how much the session holds: 308 with "Range: bytes=0-N" (no header = nothing yet).
        using var response = await SendIdempotentAsync(
            storage,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Put, session) { Content = new ByteArrayContent(Array.Empty<byte>()) };
                request.Content.Headers.TryAddWithoutValidation("Content-Range", "bytes */*");
                return request;
            },
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Gone)
        {
            throw new StorageUploadNotFoundException(uploadId);
        }

        if ((int)response.StatusCode != 308) response.EnsureSuccessStatusCode();

        long persisted = 0;
        if ((int)response.StatusCode == 308 && response.Headers.TryGetValues("Range", out var values))
        {
            var range = values.First();
            persisted = long.Parse(range.Substring(range.LastIndexOf('-') + 1), System.Globalization.CultureInfo.InvariantCulture) + 1;

            // Only whole chunks are ever sent before the final one (a multiple of 256 KiB), so one persisted byte cannot be real:
            // an empty session is reported as "bytes=0-0" by some emulators, and as no Range header by the service.
            if (persisted == 1) persisted = 0;
        }

        var tail = Array.Empty<byte>();
        try
        {
            using var buffer = new MemoryStream();
            await storage.DownloadObjectAsync(_bucketName, TailName(id), buffer, cancellationToken: cancellationToken).ConfigureAwait(false);
            tail = buffer.ToArray();
        }
        catch (GoogleApiException e) when (e.HttpStatusCode == HttpStatusCode.NotFound)
        {
        }

        return new UploadState(persisted, tail);
    }

    private static async Task PutRangeAsync(StorageClient storage, Uri session, long start, ReadOnlyMemory<byte> bytes, long? total, CancellationToken cancellationToken)
    {
        var totalText = total?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "*";
        var range = bytes.Length == 0 ? "bytes */" + totalText : $"bytes {start}-{start + bytes.Length - 1}/{totalText}";

        // The same range of the same session may be sent again: the session ignores bytes it already holds.
        using var response = await SendIdempotentAsync(
            storage,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Put, session) { Content = new ReadOnlyMemoryContent(bytes) };
                request.Content.Headers.TryAddWithoutValidation("Content-Range", range);
                return request;
            },
            cancellationToken).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        if (status == 308 || response.IsSuccessStatusCode) return;

        throw new IOException($"The upload session answered {status}.");
    }

    // The tail is at most one chunk: one simple upload (a single request) rather than the client library's resumable one (two or more).
    private async Task PutTailAsync(StorageClient storage, string name, byte[] bytes, CancellationToken cancellationToken)
    {
        var url = UploadBase(storage) + "b/" + Uri.EscapeDataString(_bucketName!) + "/o?uploadType=media&name=" + Uri.EscapeDataString(name);
        using var response = await SendIdempotentAsync(
            storage,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(bytes) };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue(GenericContentType);
                return request;
            },
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    // Sends a request whose repetition is harmless (a session status query, the same range of a session, an overwrite of the tail),
    // again when the connection fails before an answer arrives (a reused connection the service closed, a reset): up to 3 tries.
    private static async Task<HttpResponseMessage> SendIdempotentAsync(StorageClient storage, Func<HttpRequestMessage> create, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = create();
            try
            {
                return await storage.Service.HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < 3 && !cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private static async Task CancelSessionAsync(StorageClient storage, Uri session)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, session);
            using var response = await storage.Service.HttpClient.SendAsync(request).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
        }
    }

    private async Task DeleteQuietlyAsync(StorageClient storage, string name, CancellationToken cancellationToken)
    {
        try
        {
            await storage.DeleteObjectAsync(_bucketName, name, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (GoogleApiException e) when (e.HttpStatusCode == HttpStatusCode.NotFound)
        {
        }
    }
}
