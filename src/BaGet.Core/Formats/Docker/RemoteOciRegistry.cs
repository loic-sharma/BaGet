using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BaGet.Core;

/// <summary>
/// <see cref="IOciRegistry"/> of a <see cref="RepositoryKind.Remote"/> Docker repository: a pull-through cache of an upstream
/// registry (Docker Hub, GHCR, Quay, another BaGet, Artifactory, Harbor, a <c>registry:2</c>…).
/// </summary>
/// <remarks>
/// <para><b>Manifests.</b> A digest the repository stores is served from there (it never changes). A tag is asked of the upstream
/// (<see cref="OciUpstreamClient"/>: authentication, the metadata period, 404s for the negative period, an expired answer when the
/// upstream is down, nothing in offline mode; an expired tag is first checked with <c>HEAD</c>, which Docker Hub does not count as a
/// pull) and, with <see cref="RemoteRepositorySettings.StoreLocally"/>, kept with its tag so it is served locally from then on, also
/// while the upstream is down. The layers a manifest names are not fetched with it.</para>
/// <para><b>Blobs.</b> A blob the image holds is served from storage. Otherwise it is streamed from the upstream <i>while</i> it is
/// written to storage (a client is not kept waiting for a multi-gigabyte layer to arrive first), and kept once its digest checks:
/// a download that does not have the digest it was asked for is neither kept nor allowed to complete. A layer that another image of
/// this repository already stores is linked to the image once the upstream confirms (<c>HEAD</c>) that the image has it too, so a base
/// layer is downloaded once. A range request, a download already running for the same digest and a read-only server pass through
/// without being kept. Without <c>StoreLocally</c> everything passes through.</para>
/// <para><b>Tags</b> are the upstream's plus the stored ones. <b>Referrers, pages and search</b> cover what is stored. <b>Writes</b> (pushes,
/// deletes) are refused: a remote repository takes no changes. Docker Hub's official images are asked for under <c>library/</c>.</para>
/// </remarks>
public sealed class RemoteOciRegistry : IOciRegistry
{
    private const string NoChanges = "This repository caches an upstream registry and takes no pushes or deletes.";

    // The blobs being downloaded and kept right now ("repository:digest"): a second request for the same one passes through.
    private static readonly ConcurrentDictionary<string, byte> Caching = new();

    private readonly OciRegistryService _local;
    private readonly OciUpstreamClient _upstream;
    private readonly IRepositoryContext _repository;
    private readonly MaintenanceGate _gate;
    private readonly ILogger<RemoteOciRegistry> _logger;

    public RemoteOciRegistry(OciRegistryService local, OciUpstreamClient upstream, IRepositoryContext repository, MaintenanceGate gate, ILogger<RemoteOciRegistry> logger)
    {
        _local = local ?? throw new ArgumentNullException(nameof(local));
        _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private static OciException Refused() => new(OciErrorCode.Denied, NoChanges);

    // The stored copy of something that may not exist yet: an unknown image is "nothing stored", not an error.
    private static async Task<T?> StoredAsync<T>(Func<Task<T?>> read)
        where T : class
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (OciException e) when (e.Code == OciErrorCode.NameUnknown)
        {
            return null;
        }
    }

    // ---- reads ----------------------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken)
    {
        if (await _local.ImageExistsAsync(image, cancellationToken).ConfigureAwait(false)) return true;
        if (!OciNames.IsValidImage(image)) return false;

        var upstream = await _upstream.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (upstream == null) return false;

        var tags = await _upstream.ListTagsAsync(upstream, image, cancellationToken).ConfigureAwait(false);

        return tags.Status == OciUpstreamStatus.Found;
    }

    /// <inheritdoc/>
    public async Task<OciBlobInfo?> GetBlobInfoAsync(string image, string digest, CancellationToken cancellationToken)
    {
        if (!OciNames.IsValidDigest(digest) || !OciNames.IsValidImage(image)) return await _local.GetBlobInfoAsync(image, digest, cancellationToken).ConfigureAwait(false);

        var stored = await StoredAsync(() => _local.GetBlobInfoAsync(image, digest, cancellationToken)).ConfigureAwait(false);
        if (stored != null) return stored;

        var upstream = await _upstream.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (upstream == null) return null;

        var head = await _upstream.HeadBlobAsync(upstream, image, digest, cancellationToken).ConfigureAwait(false);
        switch (head.Status)
        {
            case OciUpstreamStatus.Found:
                // A layer this repository stores for another image is this image's too, now that the upstream says so.
                if (upstream.Settings.StoreLocally && !_gate.IsClosed && await _local.LinkRepositoryBlobAsync(image, digest, cancellationToken).ConfigureAwait(false))
                {
                    return await StoredAsync(() => _local.GetBlobInfoAsync(image, digest, cancellationToken)).ConfigureAwait(false) ?? new OciBlobInfo(digest, head.Size);
                }

                return new OciBlobInfo(digest, head.Size);

            case OciUpstreamStatus.Unavailable:
                throw new OciException(OciErrorCode.UpstreamUnavailable, head.Message ?? "The upstream registry could not be reached.");

            default:
                return null;
        }
    }

    /// <inheritdoc/>
    public async Task<OciBlobContent?> OpenBlobAsync(string image, string digest, long offset, long? length, CancellationToken cancellationToken)
    {
        if (!OciNames.IsValidDigest(digest) || !OciNames.IsValidImage(image)) return await _local.OpenBlobAsync(image, digest, offset, length, cancellationToken).ConfigureAwait(false);

        var stored = await StoredAsync(() => _local.OpenBlobAsync(image, digest, offset, length, cancellationToken)).ConfigureAwait(false);
        if (stored != null) return stored;

        var upstream = await _upstream.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (upstream == null || upstream.Settings.Offline) return null;

        var settings = upstream.Settings;
        var head = await _upstream.HeadBlobAsync(upstream, image, digest, cancellationToken).ConfigureAwait(false);
        if (head.Status == OciUpstreamStatus.Unavailable) throw new OciException(OciErrorCode.UpstreamUnavailable, head.Message ?? "The upstream registry could not be reached.");
        if (head.Status != OciUpstreamStatus.Found) return null;

        var whole = offset == 0 && (length == null || length.Value == head.Size);
        if (settings.StoreLocally && !_gate.IsClosed)
        {
            // Stored for another image and the upstream confirmed this one has it: served from here.
            if (await _local.LinkRepositoryBlobAsync(image, digest, cancellationToken).ConfigureAwait(false))
            {
                var linked = await StoredAsync(() => _local.OpenBlobAsync(image, digest, offset, length, cancellationToken)).ConfigureAwait(false);
                if (linked != null) return linked;
            }

            if (whole) return await CacheWhileStreamingAsync(upstream, image, digest, cancellationToken).ConfigureAwait(false);
        }

        return await PassThroughAsync(upstream, image, digest, offset, length, head.Size, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OciBlobContent?> PassThroughAsync(ResolvedUpstream upstream, string image, string digest, long offset, long? length, long size, CancellationToken cancellationToken)
    {
        if (offset > size) throw new ArgumentOutOfRangeException(nameof(offset));

        var count = Math.Min(length ?? size - offset, size - offset);
        var whole = offset == 0 && count == size;

        OciUpstreamBlob? blob;
        try
        {
            blob = await _upstream.OpenBlobAsync(upstream, image, digest, whole ? null : offset, whole ? null : offset + count - 1, cancellationToken).ConfigureAwait(false);
        }
        catch (OciUpstreamException e)
        {
            throw new OciException(OciErrorCode.UpstreamUnavailable, e.Message);
        }

        if (blob == null) return null;

        try
        {
            // A server that ignores Range sends everything from the start: skip to the part wanted.
            if (blob.Offset < offset) await SkipAsync(blob.Content, offset - blob.Offset, cancellationToken).ConfigureAwait(false);

            return new OciBlobContent(new LimitedStream(blob, count), digest, blob.TotalSize, offset, count);
        }
        catch
        {
            blob.Dispose();

            throw;
        }
    }

    private static async Task SkipAsync(Stream stream, long bytes, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            while (bytes > 0)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, bytes)), cancellationToken).ConfigureAwait(false);
                if (read == 0) throw new IOException("The upstream ended before the requested range.");

                bytes -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // Streams the blob to the client while writing it to storage; the blob is kept when the last byte arrives and the digest checks.
    private async Task<OciBlobContent?> CacheWhileStreamingAsync(ResolvedUpstream upstream, string image, string digest, CancellationToken cancellationToken)
    {
        var key = _repository.Current.Key + ":" + digest;
        if (!Caching.TryAdd(key, 0))
        {
            // Another request is downloading it right now: this one reads from the upstream without keeping.
            var size = (await _upstream.HeadBlobAsync(upstream, image, digest, cancellationToken).ConfigureAwait(false)).Size;

            return await PassThroughAsync(upstream, image, digest, 0, null, size, cancellationToken).ConfigureAwait(false);
        }

        OciUpstreamBlob? blob = null;
        OciBlobCacheWriter? writer = null;
        using var activity = BaGetDiagnostics.Source.StartActivity(BaGetDiagnostics.Spans.MirrorFetch);
        activity?.SetTag(BaGetDiagnostics.Tags.PackageId, image);
        activity?.SetTag(BaGetDiagnostics.Tags.PackageVersion, digest);
        try
        {
            try
            {
                blob = await _upstream.OpenBlobAsync(upstream, image, digest, null, null, cancellationToken).ConfigureAwait(false);
                activity?.SetTag(BaGetDiagnostics.Tags.Result, blob == null ? "NotFound" : "Streaming");
            }
            catch (OciUpstreamException e)
            {
                BaGetDiagnostics.Fail(activity, e);

                throw new OciException(OciErrorCode.UpstreamUnavailable, e.Message);
            }

            if (blob == null)
            {
                Caching.TryRemove(key, out _);

                return null;
            }

            try
            {
                writer = await _local.BeginBlobCacheAsync(image, digest, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // A store that cannot take the blob must not stop the pull: it passes through.
                _logger.LogWarning(e, "Could not start caching blob {Digest} in repository {Repository}", digest, _repository.Current.Name);
            }

            var stream = new CachingStream(blob, writer, digest, () => Caching.TryRemove(key, out _), _upstream, upstream, _logger);

            return new OciBlobContent(stream, digest, blob.TotalSize, 0, blob.Length);
        }
        catch
        {
            if (writer != null) await writer.DisposeAsync().ConfigureAwait(false);
            blob?.Dispose();
            Caching.TryRemove(key, out _);

            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<OciManifestContent?> GetManifestAsync(string image, string reference, CancellationToken cancellationToken)
    {
        if (!OciNames.IsValidImage(image)) return await _local.GetManifestAsync(image, reference, cancellationToken).ConfigureAwait(false);

        var isDigest = OciNames.LooksLikeDigest(reference);
        if (isDigest ? !OciNames.IsValidDigest(reference) : !OciNames.IsValidTag(reference)) return null;

        var stored = await _local.FindManifestAsync(image, reference, cancellationToken).ConfigureAwait(false);

        // A digest never changes: what is stored is the answer.
        if (isDigest && stored != null) return stored;

        var upstream = await _upstream.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (upstream == null) return stored;

        // The span of a fetch from the upstream (as a NuGet mirror fetch has): image and reference, never a URL or a credential.
        using var activity = BaGetDiagnostics.Source.StartActivity(BaGetDiagnostics.Spans.MirrorFetch);
        activity?.SetTag(BaGetDiagnostics.Tags.PackageId, image);
        activity?.SetTag(BaGetDiagnostics.Tags.PackageVersion, reference);

        var answer = await _upstream.GetManifestAsync(upstream, image, reference, stored, cancellationToken).ConfigureAwait(false);
        activity?.SetTag(BaGetDiagnostics.Tags.Result, answer.Status.ToString());
        switch (answer.Status)
        {
            case OciUpstreamStatus.Found:
                if (stored != null && string.Equals(stored.Digest, answer.Digest, StringComparison.Ordinal)) return stored;

                return await KeepAsync(upstream, image, isDigest ? null : reference, answer, cancellationToken).ConfigureAwait(false);

            case OciUpstreamStatus.Unavailable:
                // What was cached before keeps being served while the upstream is down.
                if (stored != null) return stored;

                throw new OciException(OciErrorCode.UpstreamUnavailable, answer.Message ?? "The upstream registry could not be reached.");

            default:
                // 404 or offline: a copy kept earlier stays available, as in any caching repository.
                return stored;
        }
    }

    private async Task<OciManifestContent> KeepAsync(ResolvedUpstream upstream, string image, string? tag, OciManifestAnswer answer, CancellationToken cancellationToken)
    {
        var content = answer.Content!;
        if (upstream.Settings.StoreLocally && !_gate.IsClosed)
        {
            try
            {
                await _local.StoreCachedManifestAsync(image, tag, answer.MediaType!, content, cancellationToken).ConfigureAwait(false);
                var kept = await _local.FindManifestAsync(image, tag ?? answer.Digest!, cancellationToken).ConfigureAwait(false);
                if (kept != null) return kept;
            }
            catch (OciException e)
            {
                // A document this registry cannot store (the upstream's, validated already) is still served as it came.
                _logger.LogWarning("Manifest {Digest} of {Image} was not kept in repository {Repository}: {Reason}", answer.Digest, image, _repository.Current.Name, e.Message);
            }
            catch (MaintenanceException)
            {
                // Read-only while a database copy runs: serve without keeping.
            }
        }

        string? subject = null;
        string? artifactType = null;
        try
        {
            var parsed = OciManifestParser.Parse(answer.MediaType, content);
            subject = parsed.Subject?.Digest;
            artifactType = parsed.ArtifactType;
        }
        catch (OciException)
        {
        }

        return new OciManifestContent(answer.Digest!, answer.MediaType!, content, subject, artifactType, DateTime.UtcNow);
    }

    /// <inheritdoc/>
    public async Task<OciTagList?> ListTagsAsync(string image, int? count, string? last, CancellationToken cancellationToken)
    {
        if (!OciNames.IsValidImage(image)) return await _local.ListTagsAsync(image, count, last, cancellationToken).ConfigureAwait(false);

        var stored = await _local.ListTagsAsync(image, null, null, cancellationToken).ConfigureAwait(false);
        var upstream = await _upstream.ResolveAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<string>? names = stored?.Tags;
        if (upstream != null)
        {
            var answer = await _upstream.ListTagsAsync(upstream, image, cancellationToken).ConfigureAwait(false);
            if (answer.Status == OciUpstreamStatus.Found)
            {
                names = answer.Tags!.Concat(stored?.Tags ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToList();
            }
            else if (answer.Status == OciUpstreamStatus.Unavailable && stored == null)
            {
                throw new OciException(OciErrorCode.UpstreamUnavailable, answer.Message ?? "The upstream registry could not be reached.");
            }
        }

        if (names == null) return null;

        return Page(image, names, count, last);
    }

    // The tags after 'last', at most 'count', in ordinal order: the page rule of the local registry.
    internal static OciTagList Page(string image, IReadOnlyList<string> names, int? count, string? last)
    {
        IEnumerable<string> ordered = names.OrderBy(n => n, StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(last)) ordered = ordered.Where(n => string.CompareOrdinal(n, last) > 0);

        var all = ordered.ToList();
        if (count == null) return new OciTagList(image, all, HasMore: false);

        var page = all.Take(Math.Max(count.Value, 0)).ToList();

        return new OciTagList(image, page, HasMore: all.Count > page.Count);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<OciDescriptor>?> GetReferrersAsync(string image, string digest, string? artifactType, CancellationToken cancellationToken)
        => _local.GetReferrersAsync(image, digest, artifactType, cancellationToken);

    /// <inheritdoc/>
    public Task<OciUploadState?> GetUploadAsync(string image, string reference, CancellationToken cancellationToken) => Task.FromResult<OciUploadState?>(null);

    /// <inheritdoc/>
    public async Task<ResolutionOutcome> ProbeAsync(string image, string reference, CancellationToken cancellationToken)
    {
        if (await _local.ProbeAsync(image, reference, cancellationToken).ConfigureAwait(false) == ResolutionOutcome.Stored) return ResolutionOutcome.Stored;

        var upstream = await _upstream.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (upstream == null) return ResolutionOutcome.NotFound;

        return await _upstream.ProbeManifestAsync(upstream, image, reference, cancellationToken).ConfigureAwait(false) switch
        {
            OciUpstreamStatus.Found => ResolutionOutcome.WouldFetch,
            OciUpstreamStatus.Offline => ResolutionOutcome.Offline,
            OciUpstreamStatus.Unavailable => ResolutionOutcome.Unavailable,
            _ => ResolutionOutcome.NotFound,
        };
    }

    // ---- writes: refused ------------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task<OciUploadState> StartUploadAsync(string image, CancellationToken cancellationToken) => throw Refused();

    /// <inheritdoc/>
    public Task<OciUploadState> AppendAsync(string image, string reference, long? offset, Stream chunk, CancellationToken cancellationToken) => throw Refused();

    /// <inheritdoc/>
    public Task<OciBlobStored> CompleteUploadAsync(string image, string reference, string digest, long? offset, Stream? lastChunk, CancellationToken cancellationToken) => throw Refused();

    /// <inheritdoc/>
    public Task<bool> CancelUploadAsync(string image, string reference, CancellationToken cancellationToken) => throw Refused();

    /// <inheritdoc/>
    public Task<OciBlobStored> PutBlobAsync(string image, string digest, Stream content, CancellationToken cancellationToken) => throw Refused();

    /// <inheritdoc/>
    public Task<bool> MountBlobAsync(string image, string digest, string fromImage, CancellationToken cancellationToken) => throw Refused();

    /// <inheritdoc/>
    public Task<OciManifestPut> PutManifestAsync(string image, string reference, string? mediaType, byte[] content, CancellationToken cancellationToken) => throw Refused();

    /// <inheritdoc/>
    public Task<bool> DeleteManifestAsync(string image, string digest, CancellationToken cancellationToken) => throw Refused();

    /// <inheritdoc/>
    public Task<bool> DeleteTagAsync(string image, string tag, CancellationToken cancellationToken) => throw Refused();

    /// <inheritdoc/>
    public Task<bool> DeleteBlobAsync(string image, string digest, CancellationToken cancellationToken) => throw Refused();

    /// <inheritdoc/>
    public Task<bool> DeleteImageAsync(string image, CancellationToken cancellationToken) => throw Refused();

    /// <inheritdoc/>
    public Task<OciPruneResult> PruneAsync(string image, CancellationToken cancellationToken) => _local.PruneAsync(image, cancellationToken);

    // ---- the pages: what is stored --------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task<OciImagePage> ListImagesAsync(string? text, int skip, int take, Func<string, CancellationToken, Task<bool>>? canRead, CancellationToken cancellationToken)
        => _local.ListImagesAsync(text, skip, take, canRead, cancellationToken);

    /// <inheritdoc/>
    public Task<OciImageInfo?> GetImageAsync(string image, CancellationToken cancellationToken) => _local.GetImageAsync(image, cancellationToken);

    /// <inheritdoc/>
    public Task<OciManifestDetail?> GetManifestDetailAsync(string image, string digest, CancellationToken cancellationToken) => _local.GetManifestDetailAsync(image, digest, cancellationToken);

    // ---- streams --------------------------------------------------------------------------------------------------------

    // The bytes of a pass-through download, at most 'limit' of them, releasing the connection on disposal.
    private sealed class LimitedStream : Stream
    {
        private readonly OciUpstreamBlob _blob;
        private long _left;

        public LimitedStream(OciUpstreamBlob blob, long limit)
        {
            _blob = blob;
            _left = limit;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_left <= 0) return 0;

            var read = _blob.Content.Read(buffer.Slice(0, (int)Math.Min(buffer.Length, _left)));
            _left -= read;

            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_left <= 0) return 0;

            var read = await _blob.Content.ReadAsync(buffer.Slice(0, (int)Math.Min(buffer.Length, _left)), cancellationToken).ConfigureAwait(false);
            _left -= read;

            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _blob.Dispose();

            base.Dispose(disposing);
        }
    }

    // Reads the upstream's bytes for the client and appends the same bytes to the store; at the end of the data the digest is checked and
    // the blob recorded. A failure of the store never fails the download (the blob is just not kept); a digest that does not match the
    // one asked for fails it (the client would reject the bytes anyway) and keeps nothing.
    private sealed class CachingStream : Stream
    {
        private const int FlushSize = 8 * 1024 * 1024;

        private readonly OciUpstreamBlob _blob;
        private readonly string _digest;
        private readonly Action _release;
        private readonly OciUpstreamClient _client;
        private readonly ResolvedUpstream _upstream;
        private readonly ILogger _logger;
        private OciBlobCacheWriter? _writer;
        private byte[]? _pending;
        private int _pendingLength;
        private long _received;
        private bool _finished;
        private int _released;

        public CachingStream(OciUpstreamBlob blob, OciBlobCacheWriter? writer, string digest, Action release, OciUpstreamClient client, ResolvedUpstream upstream, ILogger logger)
        {
            _blob = blob;
            _writer = writer;
            _digest = digest;
            _release = release;
            _client = client;
            _upstream = upstream;
            _logger = logger;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _blob.Content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read > 0)
            {
                _received += read;
                if (_writer != null) await AppendAsync(buffer.Slice(0, read), cancellationToken).ConfigureAwait(false);

                // The last bytes are held back until the blob is stored: a client that has them all may go on at once (and ask for the
                // layer again), and a digest that does not match must reach it as a download that ends in error, not as a finished one.
                if (_received >= _blob.Length && !_finished)
                {
                    _finished = true;
                    await FinishAsync(cancellationToken).ConfigureAwait(false);
                }

                return read;
            }

            if (!_finished)
            {
                _finished = true;
                await FinishAsync(cancellationToken).ConfigureAwait(false);
            }

            return 0;
        }

        private async Task AppendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        {
            try
            {
                _pending ??= ArrayPool<byte>.Shared.Rent(FlushSize);
                while (!data.IsEmpty)
                {
                    var take = Math.Min(data.Length, FlushSize - _pendingLength);
                    data.Slice(0, take).CopyTo(_pending.AsMemory(_pendingLength));
                    _pendingLength += take;
                    data = data.Slice(take);

                    if (_pendingLength == FlushSize) await FlushPendingAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                await GiveUpAsync(e).ConfigureAwait(false);
            }
        }

        private async Task FlushPendingAsync(CancellationToken cancellationToken)
        {
            if (_pendingLength == 0 || _writer == null) return;

            await _writer.AppendAsync(_pending.AsMemory(0, _pendingLength), cancellationToken).ConfigureAwait(false);
            _pendingLength = 0;
        }

        private async Task GiveUpAsync(Exception e)
        {
            _logger.LogWarning(e, "Caching blob {Digest} failed; it is passed through without being kept", _digest);
            var writer = _writer;
            _writer = null;
            if (writer != null) await writer.DisposeAsync().ConfigureAwait(false);
        }

        private async Task FinishAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_writer != null)
                {
                    try
                    {
                        await FlushPendingAsync(cancellationToken).ConfigureAwait(false);
                        if (_received != _blob.Length) throw new IOException("The upstream sent fewer bytes than it announced.");

                        if (!await _writer.CompleteAsync(cancellationToken).ConfigureAwait(false))
                        {
                            _writer = null;
                            _client.Rejected(_upstream, "A blob the upstream sent does not have the digest it was asked for.");

                            throw new IOException("The upstream sent a blob that does not have the digest asked for.");
                        }

                        _writer = null;
                    }
                    catch (Exception e) when (e is not OperationCanceledException and not IOException)
                    {
                        await GiveUpAsync(e).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                Release();
            }
        }

        private void Release()
        {
            if (_pending != null)
            {
                ArrayPool<byte>.Shared.Return(_pending);
                _pending = null;
            }

            if (Interlocked.Exchange(ref _released, 1) == 0) _release();
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask DisposeAsync() => await CleanupAsync().ConfigureAwait(false);

        protected override void Dispose(bool disposing)
        {
            if (disposing) CleanupAsync().GetAwaiter().GetResult();

            base.Dispose(disposing);
        }

        private async Task CleanupAsync()
        {
            // Not read to the end (the client went away): what was received is discarded.
            var writer = _writer;
            _writer = null;
            if (writer != null)
            {
                try
                {
                    await writer.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _logger.LogDebug(e, "Discarding the partial blob {Digest} failed", _digest);
                }
            }

            _blob.Dispose();
            Release();
        }
    }
}
