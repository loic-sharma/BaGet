using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core;

/// <summary>
/// Docker's <see cref="IFormatReplicator"/>. An item is a <b>tag</b> of an image (name = the image, version = the tag, digest = the
/// manifest's digest, which is equal on two servers holding the same image, so a tag that points at different manifests is a conflict).
/// Replicating a tag copies the manifest or index, its child manifests and every blob they use that the receiver does not hold, through
/// the Distribution Spec the peer speaks (<c>HEAD</c> a blob, monolithic <c>POST …/blobs/uploads/?digest=</c>, <c>PUT</c> manifests children
/// first, the tag last). Blobs are streamed from the store to the socket (or the other way); memory is never a layer. Referrers and
/// untagged manifests are not replicated unless a tagged index names them. Deleting a tag at the peer is <c>DELETE …/manifests/{tag}</c>.
/// </summary>
public sealed class DockerReplicator : IFormatReplicator
{
    private static readonly string[] ManifestTypes = OciNames.ImageManifestTypes.Concat(OciNames.IndexTypes).ToArray();

    private readonly IContext _context;
    private readonly IOciRegistry _registry;
    private readonly IScanGate _scanGate;

    public DockerReplicator(IContext context, IOciRegistry registry, IScanGate scanGate)
    {
        _scanGate = scanGate ?? throw new ArgumentNullException(nameof(scanGate));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <inheritdoc/>
    public RepositoryFormat Format => RepositoryFormat.Docker;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ReplicaItem>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await (from t in _context.OciTags!.AsNoTracking()
                          join m in _context.OciManifests!.AsNoTracking() on t.ManifestKey equals m.Key
                          join i in _context.OciRepositories!.AsNoTracking() on m.ImageKey equals i.Key
                          select new { Image = i.Name, Tag = t.Name, m.Digest, m.Size }).ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows
            .Select(r => new ReplicaItem(r.Image!, r.Tag!, string.Empty, r.Size, r.Digest ?? string.Empty))
            .OrderBy(i => i.Name, StringComparer.Ordinal).ThenBy(i => i.Version, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>A tag is moved by the manifest <c>PUT</c>, so a replacing push deletes nothing at the peer first.</summary>
    public bool PushReplacesInPlace => true;

    private static Uri Url(ReplicationPeer peer, ReplicaItem item, string tail) => peer.Root($"v2/{Uri.EscapeDataString(peer.Repository)}/{item.Name}/{tail}");

    /// <inheritdoc/>
    public async Task<long> PushAsync(ReplicaItem item, ReplicationPeer peer, CancellationToken cancellationToken)
    {
        var root = await _registry.GetManifestAsync(item.Name, item.Version, cancellationToken).ConfigureAwait(false)
            ?? throw new ReplicationItemException($"The tag {item} is listed but its manifest is no longer stored.");

        // Children and blobs first, then the document that names them, the tag last (the receiver checks references).
        var order = new List<OciManifestContent>();
        await CollectLocalAsync(item, root, order, cancellationToken).ConfigureAwait(false);

        long sent = 0;
        var pushedBlobs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var manifest in order)
        {
            var parsed = OciManifestParser.Parse(manifest.MediaType, manifest.Content);
            foreach (var digest in parsed.BlobDigests)
            {
                if (pushedBlobs.Add(digest)) sent += await PushBlobAsync(item, peer, digest, cancellationToken).ConfigureAwait(false);
            }

            var reference = ReferenceEquals(manifest, root) ? item.Version : manifest.Digest;
            var put = new HttpRequestMessage(HttpMethod.Put, Url(peer, item, "manifests/" + Uri.EscapeDataString(reference))) { Content = new ByteArrayContent(manifest.Content) };
            put.Content.Headers.ContentType = new MediaTypeHeaderValue(manifest.MediaType);
            await ReplicationTransfer.SendAsync(peer, put, "manifest push", item, cancellationToken).ConfigureAwait(false);
            sent += manifest.Content.Length;
        }

        return sent;
    }

    private async Task CollectLocalAsync(ReplicaItem item, OciManifestContent manifest, List<OciManifestContent> order, CancellationToken cancellationToken)
    {
        var parsed = OciManifestParser.Parse(manifest.MediaType, manifest.Content);
        foreach (var child in parsed.Children)
        {
            var content = await _registry.GetManifestAsync(item.Name, child.Descriptor.Digest, cancellationToken).ConfigureAwait(false)
                ?? throw new ReplicationItemException($"The index {item} names the manifest {child.Descriptor.Digest}, which is not stored.");
            if (order.All(m => m.Digest != content.Digest)) await CollectLocalAsync(item, content, order, cancellationToken).ConfigureAwait(false);
        }

        order.Add(manifest);
    }

    private async Task<long> PushBlobAsync(ReplicaItem item, ReplicationPeer peer, string digest, CancellationToken cancellationToken)
    {
        using (var head = await peer.SendAsync(new HttpRequestMessage(HttpMethod.Head, Url(peer, item, "blobs/" + digest)), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            if (head.IsSuccessStatusCode) return 0;
        }

        using var blob = await _registry.OpenBlobAsync(item.Name, digest, 0, null, cancellationToken).ConfigureAwait(false)
            ?? throw new ReplicationItemException($"The blob {digest} of {item} is not stored.");
        var content = new StreamContent(blob.Content, 81920);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Headers.ContentLength = blob.Length;
        var post = new HttpRequestMessage(HttpMethod.Post, Url(peer, item, "blobs/uploads/?digest=" + Uri.EscapeDataString(digest))) { Content = content };
        await ReplicationTransfer.SendAsync(peer, post, "blob push", item, cancellationToken).ConfigureAwait(false);

        return blob.Length;
    }

    /// <inheritdoc/>
    public async Task<long> PullAsync(ReplicaItem item, ReplicationPeer peer, CancellationToken cancellationToken)
    {
        var root = await GetPeerManifestAsync(item, peer, item.Version, cancellationToken).ConfigureAwait(false);

        var order = new List<OciManifestContent>();
        await CollectPeerAsync(item, peer, root, order, cancellationToken).ConfigureAwait(false);

        long received = 0;
        var pulledBlobs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var manifest in order)
        {
            var parsed = OciManifestParser.Parse(manifest.MediaType, manifest.Content);
            foreach (var digest in parsed.BlobDigests)
            {
                if (pulledBlobs.Add(digest)) received += await PullBlobAsync(item, peer, digest, cancellationToken).ConfigureAwait(false);
            }

            var reference = ReferenceEquals(manifest, root) ? item.Version : manifest.Digest;
            OciManifestPut put;
            try
            {
                put = await _registry.PutManifestAsync(item.Name, reference, manifest.MediaType, manifest.Content, cancellationToken).ConfigureAwait(false);
            }
            catch (OciException e)
            {
                throw new ReplicationItemException($"Storing {item} failed: {e.Message}", inner: e);
            }

            // The same rule as a push to the registry: an image is judged once stored (and a scan queued when nothing refuses it), and a
            // refused one is taken back. Indexes and artifacts whose config is not an image config are not images (`subject`/`artifactType` are ignored).
            if (DockerFormatScanner.IsImage(manifest.MediaType, System.Text.Encoding.UTF8.GetString(manifest.Content)))
            {
                try
                {
                    await _scanGate.CheckStoredAsync(item.Name, put.Digest, cancellationToken).ConfigureAwait(false);
                }
                catch (ScanBlockedException e)
                {
                    await OciPushUndo.UndoAsync(_registry, item.Name, reference, put).ConfigureAwait(false);

                    throw new ReplicationItemException($"The scanning policy refused {item}: {e.Message}", inner: e);
                }
            }

            received += manifest.Content.Length;
        }

        return received;
    }

    private static async Task<OciManifestContent> GetPeerManifestAsync(ReplicaItem item, ReplicationPeer peer, string reference, CancellationToken cancellationToken)
    {
        using var response = await ReplicationTransfer.GetAsync(peer, Url(peer, item, "manifests/" + Uri.EscapeDataString(reference)), item, cancellationToken,
            r => { foreach (var type in ManifestTypes) r.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(type)); }).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        var digest = OciNames.DigestOf(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant());

        return new OciManifestContent(digest, mediaType, bytes, null, null, DateTime.UtcNow);
    }

    private static async Task CollectPeerAsync(ReplicaItem item, ReplicationPeer peer, OciManifestContent manifest, List<OciManifestContent> order, CancellationToken cancellationToken)
    {
        OciParsedManifest parsed;
        try
        {
            parsed = OciManifestParser.Parse(manifest.MediaType, manifest.Content);
        }
        catch (OciException e)
        {
            throw new ReplicationItemException($"The peer's manifest of {item} is not valid: {e.Message}", inner: e);
        }

        foreach (var child in parsed.Children)
        {
            if (order.Any(m => m.Digest == child.Descriptor.Digest)) continue;

            var content = await GetPeerManifestAsync(item, peer, child.Descriptor.Digest, cancellationToken).ConfigureAwait(false);
            await CollectPeerAsync(item, peer, content, order, cancellationToken).ConfigureAwait(false);
        }

        order.Add(manifest);
    }

    private async Task<long> PullBlobAsync(ReplicaItem item, ReplicationPeer peer, string digest, CancellationToken cancellationToken)
    {
        // A first pull of an image: the image does not exist locally yet, so it holds no blob.
        if (await _registry.ImageExistsAsync(item.Name, cancellationToken).ConfigureAwait(false)
            && await _registry.GetBlobInfoAsync(item.Name, digest, cancellationToken).ConfigureAwait(false) != null)
        {
            return 0;
        }

        using var response = await ReplicationTransfer.GetAsync(peer, Url(peer, item, "blobs/" + digest), item, cancellationToken).ConfigureAwait(false);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var spooled = await ReplicationTransfer.SpoolAsync(body, cancellationToken).ConfigureAwait(false);
        try
        {
            await _registry.PutBlobAsync(item.Name, digest, spooled, cancellationToken).ConfigureAwait(false);
        }
        catch (OciException e)
        {
            throw new ReplicationItemException($"Storing the blob {digest} of {item} failed: {e.Message}", inner: e);
        }

        return spooled.Length;
    }

    /// <summary>
    /// A tag is not immutable: pulling the peer's manifest moves it, and a refused image puts the previous manifest back
    /// (<see cref="OciPushUndo"/>), so nothing is deleted first.
    /// </summary>
    public Task<long> PullReplacingAsync(ReplicaItem item, ReplicationPeer peer, CancellationToken cancellationToken)
        => PullAsync(item, peer, cancellationToken);

    /// <inheritdoc/>
    public async Task DeleteLocalAsync(ReplicaItem item, CancellationToken cancellationToken)
        => await _registry.DeleteTagAsync(item.Name, item.Version, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task DeletePeerAsync(ReplicaItem item, ReplicationPeer peer, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, Url(peer, item, "manifests/" + Uri.EscapeDataString(item.Version)));
        await ReplicationTransfer.SendAsync(peer, request, "tag delete", item, cancellationToken, allowNotFound: true).ConfigureAwait(false);
    }
}
