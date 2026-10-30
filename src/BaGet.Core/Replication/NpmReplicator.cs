using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core;

/// <summary>
/// npm's <see cref="IFormatReplicator"/>. Items are versions; the digest is the tarball's <c>integrity</c>, so a version held with a
/// different tarball on the two sides is a conflict. A push is the registry's own publish (<c>PUT {name}</c> with the tarball as a
/// base64 attachment, streamed from a temporary file so memory is the metadata, never the tarball); a pull reads the peer's packument
/// and tarball like a client and publishes them locally. The dist-tags that point at a version travel with it; moving a tag later,
/// without a new version, is not replicated. Deleting a version at the peer is <c>npm unpublish</c>'s own requests.
/// </summary>
public sealed class NpmReplicator : IFormatReplicator
{
    private const string Placeholder = "replica";

    private readonly IContext _context;
    private readonly NpmRegistryService _registry;

    public NpmReplicator(IContext context, NpmRegistryService registry)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <inheritdoc/>
    public RepositoryFormat Format => RepositoryFormat.Npm;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ReplicaItem>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await (from v in _context.NpmVersions!.AsNoTracking()
                          join p in _context.NpmPackages!.AsNoTracking() on v.PackageKey equals p.Key
                          select new { p.Name, v.Version, v.Integrity, v.TarballSize }).ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows
            .Select(r => new ReplicaItem(r.Name!, r.Version!, string.Empty, r.TarballSize, r.Integrity ?? string.Empty))
            .OrderBy(i => i.Name, StringComparer.Ordinal).ThenBy(i => i.Version, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<long> PushAsync(ReplicaItem item, ReplicationPeer peer, CancellationToken cancellationToken)
    {
        var package = await _registry.GetPackageAsync(item.Name, cancellationToken).ConfigureAwait(false);
        var version = package?.Versions.FirstOrDefault(v => v.Version == item.Version)
            ?? throw new ReplicationItemException($"The version {item} is listed but no longer stored.");
        var file = NpmNames.TarballFileName(item.Name, item.Version);
        var opened = await _registry.OpenTarballAsync(item.Name, file, cancellationToken).ConfigureAwait(false)
            ?? throw new ReplicationItemException($"The tarball of {item} is missing from the store.");
        await using var tarball = opened.Content;

        var tags = package!.DistTags.Where(t => t.Value == item.Version).Select(t => t.Key).OrderBy(t => t, StringComparer.Ordinal).ToList();
        var publishTag = tags.Count > 0 ? tags[0] : Placeholder;

        var manifest = (JsonObject)version.Manifest.DeepClone();
        manifest.Remove("_attachments");
        if (manifest["dist"] is JsonObject dist) dist.Remove("tarball");

        long length;
        using (var body = await BuildPublishBodyAsync(item, manifest, publishTag, file, tarball, cancellationToken).ConfigureAwait(false))
        {
            var request = new HttpRequestMessage(HttpMethod.Put, peer.Feed("npm/" + Key(item.Name))) { Content = ReplicationTransfer.Binary(body.File, "application/json") };
            await ReplicationTransfer.SendAsync(peer, request, "publish", item, cancellationToken).ConfigureAwait(false);
            length = body.TarballLength;
        }

        // The other tags of this version, and the removal of the placeholder when nothing named the version.
        foreach (var tag in tags.Skip(1))
        {
            var set = new HttpRequestMessage(HttpMethod.Put, peer.Feed($"npm/-/package/{Key(item.Name)}/dist-tags/{Uri.EscapeDataString(tag)}"))
            {
                Content = new StringContent(JsonSerializer.Serialize(item.Version), Encoding.UTF8, "application/json"),
            };
            await ReplicationTransfer.SendAsync(peer, set, "tag", item, cancellationToken).ConfigureAwait(false);
        }

        if (tags.Count == 0)
        {
            var remove = new HttpRequestMessage(HttpMethod.Delete, peer.Feed($"npm/-/package/{Key(item.Name)}/dist-tags/{Placeholder}"));
            await ReplicationTransfer.SendAsync(peer, remove, "tag removal", item, cancellationToken, allowNotFound: true).ConfigureAwait(false);
        }

        return length;
    }

    /// <inheritdoc/>
    public Task<long> PullAsync(ReplicaItem item, ReplicationPeer peer, CancellationToken cancellationToken)
        => PullCoreAsync(item, peer, replace: false, cancellationToken);

    /// <summary>Fetches and judges the version (integrity, scanning policy) first; only then purges the local copy and stores the peer's.</summary>
    public Task<long> PullReplacingAsync(ReplicaItem item, ReplicationPeer peer, CancellationToken cancellationToken)
        => PullCoreAsync(item, peer, replace: true, cancellationToken);

    private async Task<long> PullCoreAsync(ReplicaItem item, ReplicationPeer peer, bool replace, CancellationToken cancellationToken)
    {
        JsonObject packument;
        using (var response = await ReplicationTransfer.GetAsync(peer, peer.Feed("npm/" + Key(item.Name)), item, cancellationToken, r => r.Headers.TryAddWithoutValidation("Accept", "application/json")).ConfigureAwait(false))
        {
            packument = await response.Content.ReadJsonObjectAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new ReplicationItemException($"The peer's packument of {item.Name} is not a JSON object.");
        }

        if ((packument["versions"] as JsonObject)?[item.Version] is not JsonObject manifest)
        {
            throw new ReplicationItemException($"The peer no longer has {item}.");
        }

        var tags = (packument["dist-tags"] as JsonObject)?
            .Where(t => t.Value is JsonValue v && v.TryGetValue<string>(out var tagged) && tagged == item.Version)
            .Select(t => t.Key).OrderBy(t => t, StringComparer.Ordinal).ToList() ?? new List<string>();
        var publishTag = tags.Count > 0 ? tags[0] : Placeholder;

        var file = NpmNames.TarballFileName(item.Name, item.Version);
        long length;
        using (var response = await ReplicationTransfer.GetAsync(peer, peer.Feed($"npm/{Key(item.Name)}/-/{Uri.EscapeDataString(file)}"), item, cancellationToken).ConfigureAwait(false))
        {
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var spooled = await ReplicationTransfer.SpoolAsync(body, cancellationToken).ConfigureAwait(false);
            length = spooled.Length;

            var dist = manifest["dist"] as JsonObject;
            var integrity = item.Digest.Length > 0 ? item.Digest : dist?["integrity"] is JsonValue i && i.TryGetValue<string>(out var text) ? text : null;
            if (replace)
            {
                try
                {
                    await _registry.PreflightReplacementAsync(item.Name, item.Version, (JsonObject)manifest.DeepClone(), spooled, integrity, cancellationToken).ConfigureAwait(false);
                }
                catch (ScanBlockedException e)
                {
                    throw new ReplicationItemException($"The scanning policy refused {item}: {e.Message}", inner: e);
                }
                catch (InvalidDataException e)
                {
                    throw new ReplicationItemException($"Storing {item} failed: {e.Message}", inner: e);
                }

                await DeleteLocalAsync(item, cancellationToken).ConfigureAwait(false);
            }

            var result = await _registry.PublishAsync(
                BaGetPrincipal.Superuser,
                new NpmPublication(item.Name, item.Version, (JsonObject)manifest.DeepClone(), publishTag, spooled, integrity),
                cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                throw new ReplicationItemException($"Storing {item} failed: {result.Message}", conflict: result.Status == NpmStatus.Conflict);
            }
        }

        foreach (var tag in tags.Skip(1))
        {
            await _registry.SetDistTagAsync(BaGetPrincipal.Superuser, item.Name, tag, item.Version, cancellationToken).ConfigureAwait(false);
        }

        if (tags.Count == 0) await _registry.RemoveDistTagAsync(BaGetPrincipal.Superuser, item.Name, Placeholder, cancellationToken).ConfigureAwait(false);

        return length;
    }

    /// <inheritdoc/>
    public async Task DeleteLocalAsync(ReplicaItem item, CancellationToken cancellationToken)
        => await _registry.PurgeAsync(item.Name, item.Version, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task DeletePeerAsync(ReplicaItem item, ReplicationPeer peer, CancellationToken cancellationToken)
    {
        JsonObject packument;
        using (var response = await peer.SendAsync(new HttpRequestMessage(HttpMethod.Get, peer.Feed("npm/" + Key(item.Name) + "?write=true")), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return;

            await ReplicationPeer.EnsureAsync(response, "lookup", item).ConfigureAwait(false);
            packument = await response.Content.ReadJsonObjectAsync(cancellationToken).ConfigureAwait(false) ?? new JsonObject();
        }

        var versions = packument["versions"] as JsonObject ?? new JsonObject();
        if (!versions.ContainsKey(item.Version)) return;

        var rev = packument["_rev"] is JsonValue r && r.TryGetValue<string>(out var revision) ? revision : "0";
        var file = NpmNames.TarballFileName(item.Name, item.Version);
        if (versions.Count == 1)
        {
            await ReplicationTransfer.SendAsync(peer, new HttpRequestMessage(HttpMethod.Delete, peer.Feed($"npm/{Key(item.Name)}/-rev/{Uri.EscapeDataString(rev)}")), "unpublish", item, cancellationToken, allowNotFound: true).ConfigureAwait(false);

            return;
        }

        versions.Remove(item.Version);
        var tags = packument["dist-tags"] as JsonObject ?? new JsonObject();
        foreach (var tag in tags.Where(t => t.Value is JsonValue v && v.TryGetValue<string>(out var tagged) && tagged == item.Version).Select(t => t.Key).ToList()) tags.Remove(tag);
        var put = new HttpRequestMessage(HttpMethod.Put, peer.Feed($"npm/{Key(item.Name)}/-rev/{Uri.EscapeDataString(rev)}"))
        {
            Content = new StringContent(packument.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        await ReplicationTransfer.SendAsync(peer, put, "unpublish", item, cancellationToken).ConfigureAwait(false);
        await ReplicationTransfer.SendAsync(peer, new HttpRequestMessage(HttpMethod.Delete, peer.Feed($"npm/{Key(item.Name)}/-/{Uri.EscapeDataString(file)}/-rev/{Uri.EscapeDataString(rev)}")), "unpublish", item, cancellationToken, allowNotFound: true).ConfigureAwait(false);
    }

    // The scope's slash travels as %2f (what npm sends and what the registry routes).
    private static string Key(string name) => name.Replace("/", "%2f", StringComparison.Ordinal);

    private sealed class PublishBody : IDisposable
    {
        public PublishBody(FileStream file, long tarballLength)
        {
            File = file;
            TarballLength = tarballLength;
        }

        public FileStream File { get; }

        public long TarballLength { get; }

        public void Dispose() => File.Dispose();
    }

    // The publish document with the tarball inlined as base64, written to a temporary file in one pass: the JSON around it is small,
    // the base64 goes through a streaming transform, so the tarball is never held in memory.
    private static async Task<PublishBody> BuildPublishBodyAsync(ReplicaItem item, JsonObject manifest, string tag, string file, Stream tarball, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), "baget-replication-" + Guid.NewGuid().ToString("N"));
        var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
            var name = JsonSerializer.Serialize(item.Name);
            var prefix = $"{{\"_id\":{name},\"name\":{name},\"dist-tags\":{{{JsonSerializer.Serialize(tag)}:{JsonSerializer.Serialize(item.Version)}}},"
                         + $"\"versions\":{{{JsonSerializer.Serialize(item.Version)}:{manifest.ToJsonString()}}},"
                         + $"\"_attachments\":{{{JsonSerializer.Serialize(file)}:{{\"content_type\":\"application/octet-stream\",\"data\":\"";
            var head = Encoding.UTF8.GetBytes(prefix);
            await output.WriteAsync(head, cancellationToken).ConfigureAwait(false);

            long length = 0;
            await using (var counting = new CountingStream(tarball))
            {
                // Base64 in 3-byte groups: CryptoStream with ToBase64Transform flushes the final group on dispose.
                await using (var base64 = new CryptoStream(new NonClosingStream(output), new ToBase64Transform(), CryptoStreamMode.Write))
                {
                    await counting.CopyToAsync(base64, 81920, cancellationToken).ConfigureAwait(false);
                }

                length = counting.Count;
            }

            var tail = Encoding.UTF8.GetBytes($"\",\"length\":{length}}}}}}}");
            await output.WriteAsync(tail, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Position = 0;

            return new PublishBody(output, length);
        }
        catch
        {
            await output.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }

    private sealed class CountingStream : Stream
    {
        private readonly Stream _inner;

        public CountingStream(Stream inner)
        {
            _inner = inner;
        }

        public long Count { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            Count += read;

            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            Count += read;

            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        // The wrapped stream belongs to the caller.
        protected override void Dispose(bool disposing)
        {
        }
    }

    // A CryptoStream disposes what it writes to; the file is still needed.
    private sealed class NonClosingStream : Stream
    {
        private readonly Stream _inner;

        public NonClosingStream(Stream inner)
        {
            _inner = inner;
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => _inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => _inner.WriteAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
        }
    }
}

internal static class ReplicationJson
{
    public static async Task<JsonObject?> ReadJsonObjectAsync(this HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
