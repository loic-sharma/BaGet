using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BaGet.Core;

/// <summary>
/// Counts the blob commits in progress in this process (a file committed to storage whose row is not written yet), so a sweep that
/// looks for files without a row (<see cref="OciGarbageCollector"/>) never takes a blob that is about to be recorded for an orphan.
/// </summary>
internal static class OciBlobCommits
{
    private static int _inFlight;
    private static long _started;
    private static readonly KeyedLocks Locks = new();

    /// <summary>
    /// Serialises, in this process, the paths of blobs being stored (file committed, then row written) against the same paths being
    /// removed (row deleted, then file deleted), so a removal never deletes the file of a blob an upload has just recorded.
    /// Paths are taken in a fixed order, so two callers cannot wait on each other. Dispose to release.
    /// </summary>
    public static async Task<IDisposable> LockAsync(IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        var held = new List<IDisposable>();
        try
        {
            foreach (var path in paths.Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal))
            {
                held.Add(await Locks.LockAsync(path, cancellationToken).ConfigureAwait(false));
            }
        }
        catch
        {
            foreach (var h in held) h.Dispose();

            throw;
        }

        return new Group(held);
    }

    private sealed class Group : IDisposable
    {
        private readonly List<IDisposable> _held;

        public Group(List<IDisposable> held) => _held = held;

        public void Dispose()
        {
            foreach (var h in _held) h.Dispose();
            _held.Clear();
        }
    }

    /// <summary>Commits between "file stored" and "row written" right now.</summary>
    public static int InFlight => Volatile.Read(ref _inFlight);

    /// <summary>How many commits have started in this process; a change between two reads means one began in between.</summary>
    public static long Started => Interlocked.Read(ref _started);

    /// <summary>Marks one commit as started; dispose when its row is written (or it failed).</summary>
    public static IDisposable Enter()
    {
        Interlocked.Increment(ref _started);
        Interlocked.Increment(ref _inFlight);

        return new Exit();
    }

    private sealed class Exit : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0) Interlocked.Decrement(ref _inFlight);
        }
    }
}

/// <summary>
/// A blob being cached from an upstream registry: bytes are appended as they arrive and the blob is stored (its digest verified, its
/// row and its link to the image written) by <see cref="CompleteAsync"/>. Disposing a writer that was not completed discards what was
/// received. The upload is also recorded as an upload session of the image, so a process that dies half way leaves a session the
/// next sweep (<see cref="OciOptions.UploadExpiry"/>) aborts, not a file nobody knows about.
/// </summary>
public sealed class OciBlobCacheWriter : IAsyncDisposable
{
    private readonly OciRegistryService _owner;
    private readonly IStreamingStorageService _streaming;
    private readonly int _imageKey;
    private readonly string _digest;
    private readonly string _uploadId;
    private readonly OciUpload _session;
    private readonly IDisposable _inFlight;
    private long _length;
    private bool _finished;

    internal OciBlobCacheWriter(OciRegistryService owner, IStreamingStorageService streaming, int imageKey, string digest, string uploadId, OciUpload session)
    {
        _owner = owner;
        _streaming = streaming;
        _imageKey = imageKey;
        _digest = digest;
        _uploadId = uploadId;
        _session = session;
        _inFlight = OciBlobCommits.Enter();
    }

    /// <summary>The bytes appended so far.</summary>
    public long Length => _length;

    /// <summary>Appends the next bytes.</summary>
    public async Task AppendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        if (data.IsEmpty) return;

        using var stream = new MemoryStream(data.ToArray(), writable: false);
        _length = await _streaming.AppendAsync(_uploadId, _length, stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stores the blob under its digest and records it for the image.
    /// </summary>
    /// <returns>False when the bytes do not have the digest (nothing is stored).</returns>
    public async Task<bool> CompleteAsync(CancellationToken cancellationToken)
    {
        if (_finished) return false;
        _finished = true;

        try
        {
            return await _owner.CommitCachedBlobAsync(_imageKey, _digest, _uploadId, _session, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _inFlight.Dispose();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_finished) return;
        _finished = true;

        try
        {
            await _owner.DiscardCachedBlobAsync(_uploadId, _session).ConfigureAwait(false);
        }
        finally
        {
            _inFlight.Dispose();
        }
    }
}

public partial class OciRegistryService
{
    /// <summary>
    /// A manifest of the image by tag or digest, or null when the image or the manifest is not stored. Like
    /// <see cref="GetManifestAsync"/> for a caller that cannot know whether the image exists yet (a remote repository's first request).
    /// </summary>
    public async Task<OciManifestContent?> FindManifestAsync(string image, string reference, CancellationToken cancellationToken)
    {
        if (!OciNames.IsValidImage(image) || await FindImageAsync(image, cancellationToken).ConfigureAwait(false) == null) return null;

        return await GetManifestAsync(image, reference, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stores a manifest or index fetched from an upstream registry, under <paramref name="tag"/> when given, creating the image. Unlike
    /// a push, it does not require the blobs and child manifests it names to be here: they are fetched when a client asks for them.
    /// The document is parsed and its digest computed like any pushed manifest.
    /// </summary>
    /// <exception cref="OciException">The document is not a valid manifest.</exception>
    /// <exception cref="MaintenanceException">The server is read-only while a database copy runs.</exception>
    public Task<OciManifestPut> StoreCachedManifestAsync(string image, string? tag, string mediaType, byte[] content, CancellationToken cancellationToken)
    {
        if (content == null) throw new ArgumentNullException(nameof(content));

        return PutManifestCoreAsync(image, tag ?? OciNames.DigestOf(StreamingStorage.Sha256Hex(content)), mediaType, content, checkReferences: false, cancellationToken);
    }

    /// <summary>The blob with <paramref name="digest"/> as the repository holds it (for any image), if its file is there.</summary>
    public async Task<OciBlobInfo?> FindRepositoryBlobAsync(string digest, CancellationToken cancellationToken)
    {
        RequireSha256(digest);

        var blob = await _context.OciBlobs!.AsNoTracking().FirstOrDefaultAsync(b => b.Digest == digest, cancellationToken).ConfigureAwait(false);
        if (blob == null) return null;

        return await Streaming.GetInfoAsync(BlobPath(digest), cancellationToken).ConfigureAwait(false) == null ? null : new OciBlobInfo(digest, blob.Size);
    }

    /// <summary>
    /// Makes a blob the repository already stores (for another image) available to <paramref name="image"/>, creating the image. The
    /// caller has established that the upstream holds the blob for this image too.
    /// </summary>
    /// <returns>False when the repository does not store the blob.</returns>
    public async Task<bool> LinkRepositoryBlobAsync(string image, string digest, CancellationToken cancellationToken)
    {
        var existing = await FindRepositoryBlobAsync(digest, cancellationToken).ConfigureAwait(false);
        if (existing == null) return false;

        var imageKey = await EnsureImageAsync(image, cancellationToken).ConfigureAwait(false);
        _gate.ThrowIfClosed();

        using var blobLock = await OciBlobCommits.LockAsync(new[] { BlobPath(digest) }, cancellationToken).ConfigureAwait(false);
        if (await Streaming.GetInfoAsync(BlobPath(digest), cancellationToken).ConfigureAwait(false) == null) return false;
        await RecordBlobAsync(imageKey, digest, existing.Size, cancellationToken, countReferences: true).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Starts caching a blob of <paramref name="image"/> (creating the image); null while the server is read-only, in which case the
    /// blob is passed through without being kept.
    /// </summary>
    public async Task<OciBlobCacheWriter?> BeginBlobCacheAsync(string image, string digest, CancellationToken cancellationToken)
    {
        RequireSha256(digest);
        if (_gate.IsClosed) return null;

        var imageKey = await EnsureImageAsync(image, cancellationToken).ConfigureAwait(false);
        var uploadId = await Streaming.BeginUploadAsync("application/octet-stream", cancellationToken).ConfigureAwait(false);
        var now = _time.UtcNow;
        var session = new OciUpload { ImageKey = imageKey, Reference = Guid.NewGuid().ToString("N"), StorageUploadId = uploadId, Created = now, Updated = now };
        _context.OciUploads!.Add(session);
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await TryAbortAsync(uploadId).ConfigureAwait(false);
            (_context as DbContext)?.ChangeTracker.Clear();

            throw;
        }

        return new OciBlobCacheWriter(this, Streaming, imageKey, digest, uploadId, session);
    }

    internal async Task<bool> CommitCachedBlobAsync(int imageKey, string digest, string uploadId, OciUpload session, CancellationToken cancellationToken)
    {
        using var blobLock = await OciBlobCommits.LockAsync(new[] { BlobPath(digest) }, cancellationToken).ConfigureAwait(false);
        StorageCommitResult commit;
        try
        {
            commit = await Streaming.CommitAsync(uploadId, BlobPath(digest), OciNames.HexOf(digest), cancellationToken).ConfigureAwait(false);
        }
        catch (StorageDigestMismatchException)
        {
            await ForgetSessionAsync(session).ConfigureAwait(false);

            return false;
        }

        if (commit.Result == StoragePutResult.Conflict)
        {
            _logger.LogError("The file for blob {Digest} exists with other content in repository {Repository}", digest, _repository.Current.Name);
            await ForgetSessionAsync(session).ConfigureAwait(false);

            throw new InvalidOperationException("The blob store holds different content under this digest.");
        }

        await ForgetSessionAsync(session).ConfigureAwait(false);
        await RecordBlobAsync(imageKey, digest, commit.Length, cancellationToken, countReferences: true).ConfigureAwait(false);
        await RequireFileAfterAlreadyExistsAsync(commit, imageKey, digest, cancellationToken).ConfigureAwait(false);

        return true;
    }

    internal async Task DiscardCachedBlobAsync(string uploadId, OciUpload session)
    {
        await TryAbortAsync(uploadId).ConfigureAwait(false);
        await ForgetSessionAsync(session).ConfigureAwait(false);
    }

    // Removes the session row; a context that is gone (the request ended) or a closed gate leave it for the expiry sweep.
    private async Task ForgetSessionAsync(OciUpload session)
    {
        try
        {
            _context.OciUploads!.Remove(session);
            await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is ObjectDisposedException or DbUpdateException or MaintenanceException or InvalidOperationException)
        {
            _logger.LogDebug(e, "The cache session {Reference} was left for the expiry sweep", session.Reference);
        }
    }

    // How many manifests of the image name the digest (as config or layer): the Manifests count of a new link.
    private async Task<int> CountReferencingManifestsAsync(int imageKey, string digest, CancellationToken cancellationToken)
    {
        var indexTypes = OciNames.IndexTypes.ToArray();

        return await _context.OciManifests!.AsNoTracking()
            .CountAsync(m => m.ImageKey == imageKey && !indexTypes.Contains(m.MediaType!) && m.Content!.Contains(digest), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ResolutionOutcome> ProbeAsync(string image, string reference, CancellationToken cancellationToken)
        => await FindManifestAsync(image, reference, cancellationToken).ConfigureAwait(false) != null ? ResolutionOutcome.Stored : ResolutionOutcome.NotFound;

    /// <summary>The tag's manifest digest, or null.</summary>
    public async Task<string?> FindTagDigestAsync(string image, string tag, CancellationToken cancellationToken)
    {
        if (!OciNames.IsValidImage(image) || !OciNames.IsValidTag(tag)) return null;

        return await (from i in _context.OciRepositories!.AsNoTracking()
                      join t in _context.OciTags!.AsNoTracking() on i.Key equals t.ImageKey
                      join m in _context.OciManifests!.AsNoTracking() on t.ManifestKey equals m.Key
                      where i.Name == image && t.Name == tag
                      select m.Digest).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the stored copy of one tag or digest of the image: a digest removes that manifest, a tag removes the manifest it points at
    /// (with every tag of it). The layers nothing uses are left for <see cref="PruneAsync"/> and the garbage collector.
    /// </summary>
    /// <returns>False when nothing was stored under the reference.</returns>
    public async Task<bool> DeleteReferenceAsync(string image, string reference, CancellationToken cancellationToken)
    {
        var stored = await FindManifestAsync(image, reference, cancellationToken).ConfigureAwait(false);
        if (stored == null) return false;

        return await DeleteManifestAsync(image, stored.Digest, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes what a remote repository cached: every image (<paramref name="image"/> null), one image, or one tag or digest of it. The
    /// layers a deleted manifest used are removed with the image; after a single manifest they wait for <see cref="PruneAsync"/> or the
    /// garbage collector.
    /// </summary>
    /// <returns>The number of manifests deleted.</returns>
    public async Task<int> PurgeAsync(string? image, string? reference, CancellationToken cancellationToken)
    {
        _gate.ThrowIfClosed();

        if (image != null && reference != null) return await DeleteReferenceAsync(image, reference, cancellationToken).ConfigureAwait(false) ? 1 : 0;

        var names = image != null ? new[] { image } : (await ListImageNamesAsync(cancellationToken).ConfigureAwait(false)).ToArray();
        var deleted = 0;
        foreach (var name in names)
        {
            if (!OciNames.IsValidImage(name)) continue;

            var row = await FindImageAsync(name, cancellationToken).ConfigureAwait(false);
            if (row == null) continue;

            var manifests = await _context.OciManifests!.AsNoTracking().CountAsync(m => m.ImageKey == row.Key, cancellationToken).ConfigureAwait(false);
            if (await DeleteImageAsync(name, cancellationToken).ConfigureAwait(false)) deleted += manifests;
        }

        return deleted;
    }

    /// <summary>The number of manifests and images stored, and the bytes of the distinct blobs held.</summary>
    public async Task<(int Manifests, int Images, long Bytes)> GetUsageAsync(CancellationToken cancellationToken)
    {
        var manifests = await (from m in _context.OciManifests!.AsNoTracking()
                               join i in _context.OciRepositories!.AsNoTracking() on m.ImageKey equals i.Key
                               select m.Key).CountAsync(cancellationToken).ConfigureAwait(false);
        var images = await _context.OciRepositories!.AsNoTracking().CountAsync(cancellationToken).ConfigureAwait(false);
        var bytes = await _context.OciBlobs!.AsNoTracking().SumAsync(b => (long?)b.Size, cancellationToken).ConfigureAwait(false) ?? 0;

        return (manifests, images, bytes);
    }

    /// <summary>Names of every stored image (no access filter): what a purge walks.</summary>
    public async Task<System.Collections.Generic.IReadOnlyList<string>> ListImageNamesAsync(CancellationToken cancellationToken)
        => await _context.OciRepositories!.AsNoTracking().OrderBy(i => i.Name).Select(i => i.Name!).ToListAsync(cancellationToken).ConfigureAwait(false);
}
