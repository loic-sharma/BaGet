using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>
/// The HTTP and file plumbing the format replicators share: a download spooled to a temporary file (memory is the metadata, never the
/// artifact), an upload streamed from a file or a store, and the mapping of transport failures. A transport failure
/// (<see cref="HttpRequestException"/>, a timeout) is deliberately <b>not</b> turned into a <see cref="ReplicationItemException"/>: the
/// run treats it as "the peer cannot be reached" and stops, so it can be resumed, instead of failing every remaining item.
/// </summary>
internal static class ReplicationTransfer
{
    /// <summary>GET <paramref name="url"/>, failing with a message unless it succeeds. The caller disposes the response.</summary>
    public static async Task<HttpResponseMessage> GetAsync(ReplicationPeer peer, Uri url, ReplicaItem item, CancellationToken cancellationToken, Action<HttpRequestMessage>? configure = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        configure?.Invoke(request);
        var response = await peer.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        try
        {
            await ReplicationPeer.EnsureAsync(response, "download", item).ConfigureAwait(false);

            return response;
        }
        catch
        {
            response.Dispose();

            throw;
        }
    }

    /// <summary>Copies <paramref name="source"/> to a temporary file that deletes itself when closed; positioned at the start.</summary>
    public static async Task<FileStream> SpoolAsync(Stream source, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), "baget-replication-" + Guid.NewGuid().ToString("N"));
        var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
            await source.CopyToAsync(file, 81920, cancellationToken).ConfigureAwait(false);
            file.Position = 0;

            return file;
        }
        catch
        {
            file.Dispose();

            throw;
        }
    }

    /// <summary>Sends <paramref name="request"/>; a success is returned, anything else fails with a message naming <paramref name="what"/>.</summary>
    public static async Task SendAsync(ReplicationPeer peer, HttpRequestMessage request, string what, ReplicaItem item, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using (request)
        {
            using var response = await peer.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (allowNotFound && response.StatusCode == System.Net.HttpStatusCode.NotFound) return;

            await ReplicationPeer.EnsureAsync(response, what, item).ConfigureAwait(false);
        }
    }

    /// <summary>A binary body of a known length.</summary>
    public static StreamContent Binary(Stream content, string mediaType = "application/octet-stream")
    {
        var body = new StreamContent(content, 81920);
        body.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        if (content.CanSeek) body.Headers.ContentLength = content.Length - content.Position;

        return body;
    }

    /// <summary>The length of a stream the store hands out (counted when it cannot say).</summary>
    public static async Task<long> LengthAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream.CanSeek) return stream.Length;

        long total = 0;
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0) total += read;

        return total;
    }
}
