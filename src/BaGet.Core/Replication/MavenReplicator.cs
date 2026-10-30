using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core;

/// <summary>
/// Maven's <see cref="IFormatReplicator"/>. An item is one <b>file</b> of a version (<see cref="ReplicaItem.Part"/> is the file name,
/// the digest its SHA-256), so a version whose files arrive over several runs converges and a file stored with different content on
/// the two sides is a conflict. A push is the layout's own <c>PUT</c> of the file (the peer applies its release/snapshot policy: an
/// existing release file is a 409); a pull downloads the file and stores it through the repository's own deploy path. The generated
/// <c>maven-metadata.xml</c> and checksum files are never items (each server derives them). Deleting a file at the peer is the
/// layout's <c>DELETE</c>.
/// </summary>
public sealed class MavenReplicator : IFormatReplicator
{
    private readonly IContext _context;
    private readonly MavenRepositoryService _repository;

    public MavenReplicator(IContext context, MavenRepositoryService repository)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <inheritdoc/>
    public RepositoryFormat Format => RepositoryFormat.Maven;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ReplicaItem>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await (from f in _context.MavenFiles!.AsNoTracking()
                          join v in _context.MavenVersions!.AsNoTracking() on f.VersionKey equals v.Key
                          join a in _context.MavenArtifacts!.AsNoTracking() on v.ArtifactKey equals a.Key
                          select new { a.GroupId, a.ArtifactId, v.Version, f.FileName, f.Size, f.Sha256 }).ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows
            .Select(r => new ReplicaItem(MavenNames.ArtifactName(r.GroupId!, r.ArtifactId!), r.Version!, r.FileName!, r.Size, r.Sha256 ?? string.Empty))
            .OrderBy(i => i.Name, StringComparer.Ordinal).ThenBy(i => i.Version, StringComparer.Ordinal).ThenBy(i => i.Part, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<long> PushAsync(ReplicaItem item, ReplicationPeer peer, CancellationToken cancellationToken)
    {
        var (group, artifact) = Split(item);
        var path = MavenNames.ParsePath(MavenNames.ClientPath(group, artifact, item.Version, item.Part))
            ?? throw new ReplicationItemException($"{item} is not a valid Maven path.");
        using var download = await _repository.OpenAsync(path, cancellationToken).ConfigureAwait(false)
            ?? throw new ReplicationItemException($"The file {item} is listed but no longer stored.");
        using var file = await ReplicationTransfer.SpoolAsync(download.Content, cancellationToken).ConfigureAwait(false);
        var length = file.Length;

        var request = new HttpRequestMessage(HttpMethod.Put, peer.Feed("maven/" + MavenNames.ClientPath(group, artifact, item.Version, item.Part))) { Content = ReplicationTransfer.Binary(file) };
        await ReplicationTransfer.SendAsync(peer, request, "deploy", item, cancellationToken).ConfigureAwait(false);

        return length;
    }

    /// <inheritdoc/>
    public Task<long> PullAsync(ReplicaItem item, ReplicationPeer peer, CancellationToken cancellationToken)
        => PullCoreAsync(item, peer, replace: false, cancellationToken);

    /// <summary>Fetches and judges the file (checksum, scanning policy) first; only then deletes the local copy and stores the peer's.</summary>
    public Task<long> PullReplacingAsync(ReplicaItem item, ReplicationPeer peer, CancellationToken cancellationToken)
        => PullCoreAsync(item, peer, replace: true, cancellationToken);

    private async Task<long> PullCoreAsync(ReplicaItem item, ReplicationPeer peer, bool replace, CancellationToken cancellationToken)
    {
        var (group, artifact) = Split(item);
        var relative = MavenNames.ClientPath(group, artifact, item.Version, item.Part);
        var path = MavenNames.ParsePath(relative) ?? throw new ReplicationItemException($"{item} is not a valid Maven path.");

        using var response = await ReplicationTransfer.GetAsync(peer, peer.Feed("maven/" + relative), item, cancellationToken).ConfigureAwait(false);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var spooled = await ReplicationTransfer.SpoolAsync(body, cancellationToken).ConfigureAwait(false);
        var length = spooled.Length;

        if (item.Digest.Length > 0)
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(spooled, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
            spooled.Position = 0;
            if (!string.Equals(actual, item.Digest, StringComparison.OrdinalIgnoreCase))
            {
                throw new ReplicationItemException($"The file {item} arrived with another SHA-256 than the peer announced; it was not stored.");
            }
        }

        if (replace)
        {
            try
            {
                await _repository.PreflightReplacementAsync(path, spooled, cancellationToken).ConfigureAwait(false);
            }
            catch (ScanBlockedException e)
            {
                throw new ReplicationItemException($"The scanning policy refused {item}: {e.Message}", inner: e);
            }

            await DeleteLocalAsync(item, cancellationToken).ConfigureAwait(false);
        }

        var result = await _repository.PutAsync(BaGetPrincipal.Superuser, path, spooled, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new ReplicationItemException($"Storing {item} failed: {result.Message}", conflict: result.Status == MavenStatus.Conflict);
        }

        return length;
    }

    /// <inheritdoc/>
    public async Task DeleteLocalAsync(ReplicaItem item, CancellationToken cancellationToken)
    {
        var (group, artifact) = Split(item);
        var result = await _repository.DeleteFileAsync(BaGetPrincipal.Superuser, new MavenCoordinate(group, artifact, item.Version), item.Part, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded && result.Status != MavenStatus.NotFound) throw new ReplicationItemException($"Deleting {item} failed: {result.Message}");
    }

    /// <inheritdoc/>
    public async Task DeletePeerAsync(ReplicaItem item, ReplicationPeer peer, CancellationToken cancellationToken)
    {
        var (group, artifact) = Split(item);
        var request = new HttpRequestMessage(HttpMethod.Delete, peer.Feed("maven/" + MavenNames.ClientPath(group, artifact, item.Version, item.Part)));
        await ReplicationTransfer.SendAsync(peer, request, "delete", item, cancellationToken, allowNotFound: true).ConfigureAwait(false);
    }

    private static (string Group, string Artifact) Split(ReplicaItem item)
        => MavenNames.TryParseArtifactName(item.Name, out var group, out var artifact)
            ? (group, artifact)
            : throw new ReplicationItemException($"'{item.Name}' is not a Maven group:artifact.");
}
