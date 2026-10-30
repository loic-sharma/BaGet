using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaGet.Core;

/// <summary>
/// <see cref="IOciRegistry"/> over the Oci tables and a streaming storage service. Rows live in the repository of the request
/// (the context's query filter and stamping); a blob is one file per digest under <c>docker/blobs/sha256/…</c> of the
/// repository's storage prefix, shared by every image of the repository, and is served under an image only through an
/// <see cref="OciBlobLink"/>. Manifests are rows. Every write calls <see cref="MaintenanceGate.ThrowIfClosed"/> before it stores a
/// file or a row.
/// </summary>
public partial class OciRegistryService : IOciRegistry
{
    private readonly IContext _context;
    private readonly IStorageService _storage;
    private readonly IRepositoryContext _repository;
    private readonly SystemTime _time;
    private readonly MaintenanceGate _gate;
    private readonly ILogger<OciRegistryService> _logger;
    private readonly IOptionsSnapshot<OciOptions>? _options;
    private readonly ArtifactUsageRecorder? _usage;

    /// <param name="context">The database.</param>
    /// <param name="storage">Where blobs are kept; it must stream (<see cref="IStreamingStorageService"/>).</param>
    /// <param name="repository">The repository of the request.</param>
    /// <param name="time">The clock.</param>
    /// <param name="gate">The read-only gate, checked before anything is stored or removed.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="options">The <c>Oci:*</c> options.</param>
    /// <param name="usage">Notes downloads for the retention rules; null records nothing.</param>
    public OciRegistryService(
        IContext context,
        IStorageService storage,
        IRepositoryContext repository,
        SystemTime time,
        MaintenanceGate gate,
        ILogger<OciRegistryService> logger,
        IOptionsSnapshot<OciOptions>? options = null,
        ArtifactUsageRecorder? usage = null)
    {
        _usage = usage;
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options;
    }

    private OciOptions Options => _options?.Value ?? new OciOptions();

    private IStreamingStorageService Streaming => StreamingStorage.Require(_storage, "Docker repositories");

    private string BlobPath(string digest) => RepositoryStorage.Scope(_repository.Current, OciNames.BlobPath(digest));

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static OciException UnknownImage(string image) => new(OciErrorCode.NameUnknown, $"The repository '{image}' does not exist.", image);

    private static void ValidateImage(string image)
    {
        if (!OciNames.IsValidImage(image)) throw new OciException(OciErrorCode.NameInvalid, $"'{image}' is not a valid repository name: lower-case letters, digits, '.', '_', '-' in components separated by '/'.");
    }

    private static string RequireSha256(string digest)
    {
        if (!OciNames.IsValidDigest(digest))
        {
            throw new OciException(OciErrorCode.DigestInvalid, OciNames.LooksLikeDigest(digest)
                ? "Only sha256 digests are supported."
                : "The digest is not valid: sha256:<64 lower-case hex digits>.", digest);
        }

        return digest;
    }

    private Task<OciRepository?> FindImageAsync(string image, CancellationToken cancellationToken)
        => _context.OciRepositories!.AsNoTracking().FirstOrDefaultAsync(i => i.Name == image, cancellationToken);

    private async Task<OciRepository> RequireImageAsync(string image, CancellationToken cancellationToken)
    {
        ValidateImage(image);

        return await FindImageAsync(image, cancellationToken).ConfigureAwait(false) ?? throw UnknownImage(image);
    }

    // The image is created by the first upload or manifest. Called before the work that follows starts a transaction.
    private async Task<int> EnsureImageAsync(string image, CancellationToken cancellationToken)
    {
        ValidateImage(image);

        for (var attempt = 0; ; attempt++)
        {
            var existing = await FindImageAsync(image, cancellationToken).ConfigureAwait(false);
            if (existing != null) return existing.Key;

            _gate.ThrowIfClosed();

            var now = _time.UtcNow;
            var row = new OciRepository { Name = image, Created = now, Modified = now };
            _context.OciRepositories!.Add(row);
            try
            {
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                return row.Key;
            }
            catch (DbUpdateException e) when (_context.IsUniqueConstraintViolationException(e) && attempt < 2)
            {
                // A concurrent push created the image first.
                (_context as DbContext)?.ChangeTracker.Clear();
            }
        }
    }

    // ---- reads ------------------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken)
        => OciNames.IsValidImage(image) && await FindImageAsync(image, cancellationToken).ConfigureAwait(false) != null;

    private async Task<OciBlob?> FindHeldBlobAsync(int imageKey, string digest, CancellationToken cancellationToken)
        => await (from l in _context.OciBlobLinks!.AsNoTracking()
                  join b in _context.OciBlobs!.AsNoTracking() on l.BlobKey equals b.Key
                  where l.ImageKey == imageKey && b.Digest == digest
                  select b).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<OciBlobInfo?> GetBlobInfoAsync(string image, string digest, CancellationToken cancellationToken)
    {
        RequireSha256(digest);
        var row = await RequireImageAsync(image, cancellationToken).ConfigureAwait(false);
        var blob = await FindHeldBlobAsync(row.Key, digest, cancellationToken).ConfigureAwait(false);

        return blob == null ? null : new OciBlobInfo(digest, blob.Size);
    }

    /// <inheritdoc/>
    public async Task<OciBlobContent?> OpenBlobAsync(string image, string digest, long offset, long? length, CancellationToken cancellationToken)
    {
        RequireSha256(digest);
        var row = await RequireImageAsync(image, cancellationToken).ConfigureAwait(false);
        var blob = await FindHeldBlobAsync(row.Key, digest, cancellationToken).ConfigureAwait(false);
        if (blob == null) return null;

        if (offset < 0 || offset > blob.Size) throw new ArgumentOutOfRangeException(nameof(offset));
        var count = Math.Min(length ?? blob.Size - offset, blob.Size - offset);

        var stream = await Streaming.GetRangeAsync(BlobPath(digest), offset, count, cancellationToken).ConfigureAwait(false);
        if (stream == null)
        {
            _logger.LogError("The blob {Digest} is recorded in repository {Repository} but its file is missing", digest, _repository.Current.Name);

            return null;
        }

        return new OciBlobContent(stream, digest, blob.Size, offset, count);
    }

    /// <inheritdoc/>
    public async Task<OciManifestContent?> GetManifestAsync(string image, string reference, CancellationToken cancellationToken)
    {
        var row = await RequireImageAsync(image, cancellationToken).ConfigureAwait(false);

        OciManifest? manifest;
        if (OciNames.LooksLikeDigest(reference))
        {
            if (!OciNames.IsValidDigest(reference)) return null;

            manifest = await _context.OciManifests!.AsNoTracking().FirstOrDefaultAsync(m => m.ImageKey == row.Key && m.Digest == reference, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Neither a digest nor a tag: nothing can be stored under it, so there is nothing to find.
            if (!OciNames.IsValidTag(reference)) return null;

            manifest = await (from t in _context.OciTags!.AsNoTracking()
                              join m in _context.OciManifests!.AsNoTracking() on t.ManifestKey equals m.Key
                              where t.ImageKey == row.Key && t.Name == reference
                              select m).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }

        // For the retention rules: the tag or digest was just pulled.
        if (manifest != null) _usage?.Touch(_repository.Current.Key, image, reference);

        return manifest == null
            ? null
            : new OciManifestContent(manifest.Digest!, manifest.MediaType!, Encoding.UTF8.GetBytes(manifest.Content!), manifest.SubjectDigest, manifest.ArtifactType, Utc(manifest.Created));
    }

    /// <inheritdoc/>
    public async Task<OciTagList?> ListTagsAsync(string image, int? count, string? last, CancellationToken cancellationToken)
    {
        ValidateImage(image);
        var row = await FindImageAsync(image, cancellationToken).ConfigureAwait(false);
        if (row == null) return null;

        // Compared and ordered here, by code point: the engines collate differently and a page boundary must be exact.
        var names = await _context.OciTags!.AsNoTracking().Where(t => t.ImageKey == row.Key).Select(t => t.Name!).ToListAsync(cancellationToken).ConfigureAwait(false);
        IEnumerable<string> ordered = names.OrderBy(n => n, StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(last)) ordered = ordered.Where(n => string.CompareOrdinal(n, last) > 0);

        var all = ordered.ToList();
        if (count == null) return new OciTagList(image, all, HasMore: false);

        var page = all.Take(Math.Max(count.Value, 0)).ToList();

        return new OciTagList(image, page, HasMore: all.Count > page.Count);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OciDescriptor>?> GetReferrersAsync(string image, string digest, string? artifactType, CancellationToken cancellationToken)
    {
        RequireSha256(digest);
        ValidateImage(image);
        var row = await FindImageAsync(image, cancellationToken).ConfigureAwait(false);
        if (row == null) return null;

        var query = _context.OciManifests!.AsNoTracking().Where(m => m.ImageKey == row.Key && m.SubjectDigest == digest);
        if (!string.IsNullOrEmpty(artifactType)) query = query.Where(m => m.ArtifactType == artifactType);

        var manifests = await query.OrderBy(m => m.Created).ThenBy(m => m.Digest).ToListAsync(cancellationToken).ConfigureAwait(false);

        var result = new List<OciDescriptor>();
        foreach (var manifest in manifests)
        {
            IReadOnlyDictionary<string, string>? annotations = null;
            try
            {
                var parsed = OciManifestParser.Parse(manifest.MediaType, Encoding.UTF8.GetBytes(manifest.Content!));
                if (parsed.Annotations.Count > 0) annotations = parsed.Annotations;
            }
            catch (OciException)
            {
                // Stored documents were validated on the way in; a row that no longer parses is listed without annotations.
            }

            result.Add(new OciDescriptor(manifest.MediaType!, manifest.Digest!, manifest.Size, manifest.ArtifactType, annotations));
        }

        return result;
    }

    // ---- uploads ----------------------------------------------------------------------------------------------------

    private async Task<OciUpload?> FindUploadAsync(int imageKey, string reference, bool tracked, CancellationToken cancellationToken)
    {
        if (!OciNames.IsValidUploadReference(reference)) return null;

        var query = _context.OciUploads!.Where(u => u.ImageKey == imageKey && u.Reference == reference);

        return await (tracked ? query : query.AsNoTracking()).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<OciUploadState?> GetUploadAsync(string image, string reference, CancellationToken cancellationToken)
    {
        var row = await RequireImageAsync(image, cancellationToken).ConfigureAwait(false);
        var upload = await FindUploadAsync(row.Key, reference, tracked: false, cancellationToken).ConfigureAwait(false);
        if (upload == null) return null;

        var length = await Streaming.GetUploadLengthAsync(upload.StorageUploadId!, cancellationToken).ConfigureAwait(false);

        return length == null ? null : new OciUploadState(reference, length.Value);
    }

    /// <inheritdoc/>
    public async Task<OciUploadState> StartUploadAsync(string image, CancellationToken cancellationToken)
    {
        var imageKey = await EnsureImageAsync(image, cancellationToken).ConfigureAwait(false);
        _gate.ThrowIfClosed();

        await ExpireUploadsAsync(cancellationToken).ConfigureAwait(false);

        var storageId = await Streaming.BeginUploadAsync("application/octet-stream", cancellationToken).ConfigureAwait(false);
        var now = _time.UtcNow;
        var reference = Guid.NewGuid().ToString("N");
        _context.OciUploads!.Add(new OciUpload { ImageKey = imageKey, Reference = reference, StorageUploadId = storageId, Created = now, Updated = now });
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await TryAbortAsync(storageId).ConfigureAwait(false);
            throw;
        }

        return new OciUploadState(reference, 0);
    }

    // Sessions nobody touched for Oci:UploadExpiry are discarded whenever a new one starts, so abandoned pushes do not pile up.
    private async Task ExpireUploadsAsync(CancellationToken cancellationToken)
    {
        var cutoff = _time.UtcNow - Options.UploadExpiry;
        var stale = await (from u in _context.OciUploads!
                           join i in _context.OciRepositories! on u.ImageKey equals i.Key
                           where u.Updated < cutoff
                           select u).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (stale.Count == 0) return;

        _context.OciUploads!.RemoveRange(stale);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var upload in stale) await TryAbortAsync(upload.StorageUploadId!).ConfigureAwait(false);
    }

    private async Task TryAbortAsync(string storageUploadId)
    {
        try
        {
            await Streaming.AbortAsync(storageUploadId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogWarning(e, "Could not abort the upload {UploadId}", storageUploadId);
        }
    }

    /// <inheritdoc/>
    public async Task<OciUploadState> AppendAsync(string image, string reference, long? offset, Stream chunk, CancellationToken cancellationToken)
    {
        if (chunk == null) throw new ArgumentNullException(nameof(chunk));

        var row = await RequireImageAsync(image, cancellationToken).ConfigureAwait(false);
        var upload = await FindUploadAsync(row.Key, reference, tracked: true, cancellationToken).ConfigureAwait(false)
                     ?? throw new OciException(OciErrorCode.BlobUploadUnknown, "The upload session does not exist.", reference);

        var length = await AppendCoreAsync(upload, offset, chunk, cancellationToken).ConfigureAwait(false);

        return new OciUploadState(reference, length);
    }

    private async Task<long> AppendCoreAsync(OciUpload upload, long? offset, Stream chunk, CancellationToken cancellationToken)
    {
        _gate.ThrowIfClosed();

        var current = await Streaming.GetUploadLengthAsync(upload.StorageUploadId!, cancellationToken).ConfigureAwait(false);
        if (current == null)
        {
            _context.OciUploads!.Remove(upload);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            throw new OciException(OciErrorCode.BlobUploadUnknown, "The upload session has expired.", upload.Reference);
        }

        if (offset != null && offset.Value != current.Value)
        {
            throw new OciException(OciErrorCode.RangeInvalid, $"The chunk starts at {offset.Value} but the upload holds {current.Value} bytes.", current.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)) { UploadLength = current.Value };
        }

        // An empty chunk (a PATCH with no body, a final PUT with none) adds nothing: not every store takes an empty part.
        var length = current.Value;
        var data = await OciStreams.NonEmptyAsync(chunk, cancellationToken).ConfigureAwait(false);
        try
        {
            if (data != null) length = await Streaming.AppendAsync(upload.StorageUploadId!, current.Value, data, cancellationToken).ConfigureAwait(false);
        }
        catch (StorageUploadOffsetException e)
        {
            throw new OciException(OciErrorCode.RangeInvalid, e.Message, e.ActualLength.ToString(System.Globalization.CultureInfo.InvariantCulture)) { UploadLength = e.ActualLength };
        }
        catch (StorageUploadNotFoundException)
        {
            throw new OciException(OciErrorCode.BlobUploadUnknown, "The upload session does not exist.", upload.Reference);
        }

        upload.Updated = _time.UtcNow;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return length;
    }

    /// <inheritdoc/>
    public async Task<OciBlobStored> CompleteUploadAsync(string image, string reference, string digest, long? offset, Stream? lastChunk, CancellationToken cancellationToken)
    {
        RequireSha256(digest);
        var row = await RequireImageAsync(image, cancellationToken).ConfigureAwait(false);
        var upload = await FindUploadAsync(row.Key, reference, tracked: true, cancellationToken).ConfigureAwait(false)
                     ?? throw new OciException(OciErrorCode.BlobUploadUnknown, "The upload session does not exist.", reference);
        var uploadId = upload.StorageUploadId!;

        _gate.ThrowIfClosed();
        using var inFlight = OciBlobCommits.Enter();

        if (lastChunk != null) await AppendCoreAsync(upload, offset, lastChunk, cancellationToken).ConfigureAwait(false);

        using var blobLock = await OciBlobCommits.LockAsync(new[] { BlobPath(digest) }, cancellationToken).ConfigureAwait(false);
        StorageCommitResult commit;
        try
        {
            commit = await Streaming.CommitAsync(uploadId, BlobPath(digest), OciNames.HexOf(digest), cancellationToken).ConfigureAwait(false);
        }
        catch (StorageDigestMismatchException)
        {
            // The content is discarded with the session: the client starts again.
            _context.OciUploads!.Remove(upload);
            await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

            throw new OciException(OciErrorCode.DigestInvalid, "The uploaded content does not have the digest given.", digest);
        }
        catch (StorageUploadNotFoundException)
        {
            _context.OciUploads!.Remove(upload);
            await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

            throw new OciException(OciErrorCode.BlobUploadUnknown, "The upload session does not exist.", reference);
        }

        if (commit.Result == StoragePutResult.Conflict)
        {
            _logger.LogError("The file for blob {Digest} exists with other content in repository {Repository}", digest, _repository.Current.Name);
            throw new InvalidOperationException("The blob store holds different content under this digest.");
        }

        _context.OciUploads!.Remove(upload);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await RecordBlobAsync(row.Key, digest, commit.Length, cancellationToken).ConfigureAwait(false);
        await RequireFileAfterAlreadyExistsAsync(commit, row.Key, digest, cancellationToken).ConfigureAwait(false);

        return new OciBlobStored(digest, commit.Length);
    }

    /// <inheritdoc/>
    public async Task<bool> CancelUploadAsync(string image, string reference, CancellationToken cancellationToken)
    {
        var row = await RequireImageAsync(image, cancellationToken).ConfigureAwait(false);
        var upload = await FindUploadAsync(row.Key, reference, tracked: true, cancellationToken).ConfigureAwait(false);
        if (upload == null) return false;

        _gate.ThrowIfClosed();

        _context.OciUploads!.Remove(upload);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await TryAbortAsync(upload.StorageUploadId!).ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc/>
    public async Task<OciBlobStored> PutBlobAsync(string image, string digest, Stream content, CancellationToken cancellationToken)
    {
        if (content == null) throw new ArgumentNullException(nameof(content));

        RequireSha256(digest);
        var imageKey = await EnsureImageAsync(image, cancellationToken).ConfigureAwait(false);
        _gate.ThrowIfClosed();
        using var inFlight = OciBlobCommits.Enter();

        // The body is read before the digest is locked: a slow client must not hold up deletes, the GC or other pushes.
        var uploadId = await Streaming.BeginUploadAsync("application/octet-stream", cancellationToken).ConfigureAwait(false);
        try
        {
            var data = await OciStreams.NonEmptyAsync(content, cancellationToken).ConfigureAwait(false);
            if (data != null) await Streaming.AppendAsync(uploadId, 0, data, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await TryAbortAsync(uploadId).ConfigureAwait(false);
            throw;
        }

        using var blobLock = await OciBlobCommits.LockAsync(new[] { BlobPath(digest) }, cancellationToken).ConfigureAwait(false);
        StorageCommitResult commit;
        try
        {
            commit = await Streaming.CommitAsync(uploadId, BlobPath(digest), OciNames.HexOf(digest), cancellationToken).ConfigureAwait(false);
        }
        catch (StorageDigestMismatchException)
        {
            throw new OciException(OciErrorCode.DigestInvalid, "The uploaded content does not have the digest given.", digest);
        }
        catch
        {
            await TryAbortAsync(uploadId).ConfigureAwait(false);
            throw;
        }

        if (commit.Result == StoragePutResult.Conflict) throw new InvalidOperationException("The blob store holds different content under this digest.");

        await RecordBlobAsync(imageKey, digest, commit.Length, cancellationToken).ConfigureAwait(false);
        await RequireFileAfterAlreadyExistsAsync(commit, imageKey, digest, cancellationToken).ConfigureAwait(false);

        return new OciBlobStored(digest, commit.Length);
    }

    // A commit onto an existing file reports AlreadyExists; the file may have been removed (by another node, which the in-process
    // lock cannot see) after the row was written. Look once more: if it is gone, undo the record (else a retrying client's HEAD would
    // find the row, skip the upload and the blob would be a 404) and fail so the client uploads again.
    private async Task RequireFileAfterAlreadyExistsAsync(StorageCommitResult commit, int imageKey, string digest, CancellationToken cancellationToken)
    {
        if (commit.Result != StoragePutResult.AlreadyExists) return;
        if (await Streaming.GetInfoAsync(BlobPath(digest), cancellationToken).ConfigureAwait(false) != null) return;

        await UnrecordBlobAsync(imageKey, digest).ConfigureAwait(false);

        throw new OciException(OciErrorCode.BlobUploadInvalid, "The blob was removed while it was being stored: upload it again.", digest);
    }

    // Takes back RecordBlobAsync for a blob whose file is gone: the image's link, and the blob row when no image holds it. The caller
    // holds the digest lock.
    private async Task UnrecordBlobAsync(int imageKey, string digest)
    {
        (_context as DbContext)?.ChangeTracker.Clear();
        await using var transaction = await _context.Database.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);

        var blob = await _context.OciBlobs!.FirstOrDefaultAsync(b => b.Digest == digest, CancellationToken.None).ConfigureAwait(false);
        if (blob == null) return;

        var link = await _context.OciBlobLinks!.FirstOrDefaultAsync(l => l.ImageKey == imageKey && l.BlobKey == blob.Key, CancellationToken.None).ConfigureAwait(false);
        if (link != null) _context.OciBlobLinks!.Remove(link);
        await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

        var holders = await _context.OciBlobLinks!.CountAsync(l => l.BlobKey == blob.Key, CancellationToken.None).ConfigureAwait(false);
        if (holders == 0) _context.OciBlobs!.Remove(blob);
        else blob.RefCount = holders;
        await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
    }

    // Records that the image holds the blob (creating the blob row when this is the repository's first copy) and settles the
    // upload session removal that the caller staged. A concurrent push of the same layer races on the unique indexes.
    private async Task RecordBlobAsync(int imageKey, string digest, long size, CancellationToken cancellationToken, bool countReferences = false)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                var blob = await _context.OciBlobs!.FirstOrDefaultAsync(b => b.Digest == digest, cancellationToken).ConfigureAwait(false);
                if (blob == null)
                {
                    blob = new OciBlob { Digest = digest, Size = size, RefCount = 0, Created = _time.UtcNow };
                    _context.OciBlobs!.Add(blob);
                    await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }

                var linked = await _context.OciBlobLinks!.AnyAsync(l => l.ImageKey == imageKey && l.BlobKey == blob.Key, cancellationToken).ConfigureAwait(false);
                if (!linked)
                {
                    // A layer cached for an image whose manifest is already stored is used by that manifest; an uploaded one is not yet.
                    var users = countReferences ? await CountReferencingManifestsAsync(imageKey, digest, cancellationToken).ConfigureAwait(false) : 0;
                    _context.OciBlobLinks!.Add(new OciBlobLink { ImageKey = imageKey, BlobKey = blob.Key, Manifests = users, Created = _time.UtcNow });
                    await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }

                blob.RefCount = await _context.OciBlobLinks!.CountAsync(l => l.BlobKey == blob.Key, cancellationToken).ConfigureAwait(false);
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return;
            }
            catch (DbUpdateException e) when (_context.IsUniqueConstraintViolationException(e) && attempt < 2)
            {
                // A concurrent upload of the same blob (or into the same image) created the row first: look again.
                (_context as DbContext)?.ChangeTracker.Clear();
            }
        }
    }

    /// <inheritdoc/>
    public async Task<bool> MountBlobAsync(string image, string digest, string fromImage, CancellationToken cancellationToken)
    {
        RequireSha256(digest);
        ValidateImage(fromImage);

        var source = await FindImageAsync(fromImage, cancellationToken).ConfigureAwait(false);
        if (source == null) return false;

        var blob = await FindHeldBlobAsync(source.Key, digest, cancellationToken).ConfigureAwait(false);
        if (blob == null) return false;

        var imageKey = await EnsureImageAsync(image, cancellationToken).ConfigureAwait(false);
        _gate.ThrowIfClosed();

        // Under the digest lock, and only while the file is there: a delete of the source image may have removed the blob since.
        using var blobLock = await OciBlobCommits.LockAsync(new[] { BlobPath(digest) }, cancellationToken).ConfigureAwait(false);
        if (await Streaming.GetInfoAsync(BlobPath(digest), cancellationToken).ConfigureAwait(false) == null) return false;
        await RecordBlobAsync(imageKey, digest, blob.Size, cancellationToken).ConfigureAwait(false);

        return true;
    }

    // ---- manifests --------------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task<OciManifestPut> PutManifestAsync(string image, string reference, string? mediaType, byte[] content, CancellationToken cancellationToken)
        => PutManifestCoreAsync(image, reference, mediaType, content, checkReferences: true, cancellationToken);

    // checkReferences false: a manifest cached from an upstream registry is stored before its layers are (they follow when a client
    // asks for them), so the blobs and children it names need not be here yet.
    private async Task<OciManifestPut> PutManifestCoreAsync(string image, string reference, string? mediaType, byte[] content, bool checkReferences, CancellationToken cancellationToken)
    {
        if (content == null) throw new ArgumentNullException(nameof(content));

        ValidateImage(image);
        var byDigest = OciNames.LooksLikeDigest(reference);
        if (byDigest) RequireSha256(reference);
        else if (!OciNames.IsValidTag(reference)) throw new OciException(OciErrorCode.TagInvalid, $"'{reference}' is not a valid tag: letters, digits, '_', '.', '-', at most 128, not starting with '.' or '-'.");

        if (content.Length > Options.MaxManifestBytes)
        {
            throw new OciException(OciErrorCode.ManifestTooLarge, $"The manifest is larger than {Options.MaxManifestBytes} bytes.");
        }

        var parsed = OciManifestParser.Parse(mediaType, content);
        var digest = OciNames.DigestOf(StreamingStorage.Sha256Hex(content));
        if (byDigest && !string.Equals(reference, digest, StringComparison.Ordinal))
        {
            throw new OciException(OciErrorCode.DigestInvalid, "The manifest does not have the digest in the URL.", digest);
        }

        var imageKey = await EnsureImageAsync(image, cancellationToken).ConfigureAwait(false);
        _gate.ThrowIfClosed();

        var text = Encoding.UTF8.GetString(content);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                var now = _time.UtcNow;

                // Everything the manifest points at must already be in the image (a client pushes layers, then children, then this).
                var wanted = parsed.IsIndex
                    ? parsed.Children.Select(c => c.Descriptor.Digest).Distinct(StringComparer.Ordinal).ToList()
                    : parsed.BlobDigests.ToList();

                List<OciBlobLink> links = new();
                if (parsed.IsIndex)
                {
                    var present = await _context.OciManifests!.AsNoTracking().Where(m => m.ImageKey == imageKey && wanted.Contains(m.Digest!)).Select(m => m.Digest!).ToListAsync(cancellationToken).ConfigureAwait(false);
                    var missing = wanted.FirstOrDefault(d => !present.Contains(d, StringComparer.Ordinal));
                    if (missing != null && checkReferences) throw new OciException(OciErrorCode.ManifestBlobUnknown, "The index references a manifest the repository does not hold.", missing);
                }
                else
                {
                    var held = await (from l in _context.OciBlobLinks!
                                      join b in _context.OciBlobs! on l.BlobKey equals b.Key
                                      where l.ImageKey == imageKey && wanted.Contains(b.Digest!)
                                      select new { Link = l, b.Digest }).ToListAsync(cancellationToken).ConfigureAwait(false);
                    var missing = wanted.FirstOrDefault(d => held.All(h => !string.Equals(h.Digest, d, StringComparison.Ordinal)));
                    if (missing != null && checkReferences) throw new OciException(OciErrorCode.ManifestBlobUnknown, "The manifest references a blob the repository does not hold.", missing);

                    links = held.Select(h => h.Link).ToList();
                }

                var manifest = await _context.OciManifests!.FirstOrDefaultAsync(m => m.ImageKey == imageKey && m.Digest == digest, cancellationToken).ConfigureAwait(false);
                var created = manifest == null;
                if (manifest == null)
                {
                    manifest = new OciManifest
                    {
                        ImageKey = imageKey,
                        Digest = digest,
                        MediaType = parsed.MediaType,
                        ArtifactType = parsed.ArtifactType,
                        SubjectDigest = parsed.Subject?.Digest,
                        Size = content.LongLength,
                        Content = text,
                        Created = now,
                    };
                    _context.OciManifests!.Add(manifest);
                    await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    // The count is raised in the database (Manifests = Manifests + 1), never written back as a number this request read:
                    // two pushes using the same layer would otherwise both write the same value and lose one reference.
                    if (links.Count > 0)
                    {
                        var linkKeys = links.Select(l => l.Key).ToList();
                        var raised = await _context.OciBlobLinks!.Where(l => linkKeys.Contains(l.Key))
                            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Manifests, l => l.Manifests + 1), cancellationToken).ConfigureAwait(false);
                        if (raised != linkKeys.Count && checkReferences)
                        {
                            // A layer link was removed (a prune) between the check and now.
                            throw new OciException(OciErrorCode.ManifestBlobUnknown, "The manifest references a blob the repository does not hold.");
                        }
                    }
                }

                string? previousDigest = null;
                if (!byDigest)
                {
                    var tag = await _context.OciTags!.FirstOrDefaultAsync(t => t.ImageKey == imageKey && t.Name == reference, cancellationToken).ConfigureAwait(false);
                    if (tag != null)
                    {
                        previousDigest = await _context.OciManifests!.AsNoTracking().Where(m => m.Key == tag.ManifestKey).Select(m => m.Digest).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                    }

                    if (tag == null)
                    {
                        _context.OciTags!.Add(new OciTag { ImageKey = imageKey, Name = reference, ManifestKey = manifest.Key, Updated = now });
                    }
                    else if (tag.ManifestKey != manifest.Key)
                    {
                        tag.ManifestKey = manifest.Key;
                        tag.Updated = now;
                    }
                }

                var imageRow = await _context.OciRepositories!.FirstAsync(i => i.Key == imageKey, cancellationToken).ConfigureAwait(false);
                imageRow.Modified = now;
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return new OciManifestPut(digest, parsed.Subject?.Digest, created, previousDigest);
            }
            catch (DbUpdateException e) when (_context.IsUniqueConstraintViolationException(e) && attempt < 2)
            {
                // A concurrent push of the same manifest or tag won; apply on top of its rows.
                (_context as DbContext)?.ChangeTracker.Clear();
            }
        }
    }

    // ---- deletes ----------------------------------------------------------------------------------------------------

    // Recomputes the holders of each blob and removes the blob (row and file) when no image holds it any more.
    // The digests are locked (OciBlobCommits) from the holder check to the file deletion: an upload of the same blob in this process
    // either records its row before the check (the blob is kept) or stores its file after the deletion.
    private async Task ReleaseBlobsAsync(IReadOnlyCollection<int> blobKeys, CancellationToken cancellationToken)
    {
        var distinct = blobKeys.Distinct().ToList();
        var digests = await _context.OciBlobs!.AsNoTracking().Where(b => distinct.Contains(b.Key)).Select(b => b.Digest!).ToListAsync(cancellationToken).ConfigureAwait(false);
        using var blobLocks = await OciBlobCommits.LockAsync(digests.Select(BlobPath), cancellationToken).ConfigureAwait(false);

        var files = new List<string>();
        foreach (var key in distinct)
        {
            var blob = await _context.OciBlobs!.FirstOrDefaultAsync(b => b.Key == key, cancellationToken).ConfigureAwait(false);
            if (blob == null) continue;

            var holders = await _context.OciBlobLinks!.CountAsync(l => l.BlobKey == key, cancellationToken).ConfigureAwait(false);
            if (holders == 0)
            {
                _context.OciBlobs!.Remove(blob);
                files.Add(BlobPath(blob.Digest!));
            }
            else
            {
                blob.RefCount = holders;
            }
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var path in files)
        {
            try
            {
                await _storage.DeleteAsync(path, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger.LogWarning(e, "Could not delete the blob file {Path}", path);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteManifestAsync(string image, string digest, CancellationToken cancellationToken)
    {
        RequireSha256(digest);
        var row = await RequireImageAsync(image, cancellationToken).ConfigureAwait(false);
        var manifest = await _context.OciManifests!.FirstOrDefaultAsync(m => m.ImageKey == row.Key && m.Digest == digest, cancellationToken).ConfigureAwait(false);
        if (manifest == null) return false;

        _gate.ThrowIfClosed();

        await DeleteManifestRowAsync(row.Key, manifest, cancellationToken).ConfigureAwait(false);

        return true;
    }

    // Deleting the row deletes its tags (foreign key); the blobs it used lose one manifest reference.
    private async Task DeleteManifestRowAsync(int imageKey, OciManifest manifest, CancellationToken cancellationToken)
    {
        var digests = new List<string>();
        try
        {
            var parsed = OciManifestParser.Parse(manifest.MediaType, Encoding.UTF8.GetBytes(manifest.Content!));
            if (!parsed.IsIndex) digests.AddRange(parsed.BlobDigests);
        }
        catch (OciException)
        {
            // A document that no longer parses had no counted references to give back.
        }

        var links = digests.Count == 0
            ? new List<OciBlobLink>()
            : await (from l in _context.OciBlobLinks!
                     join b in _context.OciBlobs! on l.BlobKey equals b.Key
                     where l.ImageKey == imageKey && digests.Contains(b.Digest!)
                     select l).ToListAsync(cancellationToken).ConfigureAwait(false);
        _gate.ThrowIfClosed();
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Removing the row first: a second delete of the same manifest fails here (nothing deleted) and gives no reference back twice.
        _context.OciManifests!.Remove(manifest);
        var image = await _context.OciRepositories!.FirstOrDefaultAsync(i => i.Key == imageKey, cancellationToken).ConfigureAwait(false);
        if (image != null) image.Modified = _time.UtcNow;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Lowered in the database (never below 0), like the raise in PutManifestCoreAsync.
        if (links.Count > 0)
        {
            var linkKeys = links.Select(l => l.Key).ToList();
            await _context.OciBlobLinks!.Where(l => linkKeys.Contains(l.Key) && l.Manifests > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.Manifests, l => l.Manifests - 1), cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteTagAsync(string image, string tag, CancellationToken cancellationToken)
    {
        var row = await RequireImageAsync(image, cancellationToken).ConfigureAwait(false);
        if (!OciNames.IsValidTag(tag)) return false;

        var existing = await _context.OciTags!.FirstOrDefaultAsync(t => t.ImageKey == row.Key && t.Name == tag, cancellationToken).ConfigureAwait(false);
        if (existing == null) return false;

        _gate.ThrowIfClosed();

        _context.OciTags!.Remove(existing);
        var imageRow = await _context.OciRepositories!.FirstOrDefaultAsync(i => i.Key == row.Key, cancellationToken).ConfigureAwait(false);
        if (imageRow != null) imageRow.Modified = _time.UtcNow;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteBlobAsync(string image, string digest, CancellationToken cancellationToken)
    {
        RequireSha256(digest);
        var row = await RequireImageAsync(image, cancellationToken).ConfigureAwait(false);
        var link = await (from l in _context.OciBlobLinks!
                          join b in _context.OciBlobs! on l.BlobKey equals b.Key
                          where l.ImageKey == row.Key && b.Digest == digest
                          select l).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (link == null) return false;

        if (link.Manifests > 0)
        {
            throw new OciException(OciErrorCode.Unsupported, "The blob is used by a manifest of this repository: delete the manifest first.", digest);
        }

        _gate.ThrowIfClosed();

        // Deleted only while no manifest uses it (the count is checked by the statement itself, not by the read above), so a manifest
        // pushed in between keeps its layer.
        var blobKey = link.BlobKey;
        var deleted = await _context.OciBlobLinks!.Where(l => l.Key == link.Key && l.Manifests == 0).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        (_context as DbContext)?.Entry(link).State = EntityState.Detached;
        if (deleted == 0)
        {
            throw new OciException(OciErrorCode.Unsupported, "The blob is used by a manifest of this repository: delete the manifest first.", digest);
        }

        await ReleaseBlobsAsync(new[] { blobKey }, cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteImageAsync(string image, CancellationToken cancellationToken)
    {
        ValidateImage(image);
        var row = await _context.OciRepositories!.FirstOrDefaultAsync(i => i.Name == image, cancellationToken).ConfigureAwait(false);
        if (row == null) return false;

        _gate.ThrowIfClosed();

        var blobKeys = await _context.OciBlobLinks!.AsNoTracking().Where(l => l.ImageKey == row.Key).Select(l => l.BlobKey).ToListAsync(cancellationToken).ConfigureAwait(false);
        var uploads = await _context.OciUploads!.AsNoTracking().Where(u => u.ImageKey == row.Key).Select(u => u.StorageUploadId!).ToListAsync(cancellationToken).ConfigureAwait(false);

        // The manifests, links and sessions go with the image (cascading foreign keys); the tags are removed here, before the image
        // (a tag also names the image, which the database must not see deleted while a tag still points at it).
        _context.OciTags!.RemoveRange(await _context.OciTags!.Where(t => t.ImageKey == row.Key).ToListAsync(cancellationToken).ConfigureAwait(false));
        _context.OciRepositories!.Remove(row);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await ReleaseBlobsAsync(blobKeys, cancellationToken).ConfigureAwait(false);
        foreach (var id in uploads) await TryAbortAsync(id).ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc/>
    public async Task<OciPruneResult> PruneAsync(string image, CancellationToken cancellationToken)
    {
        var row = await RequireImageAsync(image, cancellationToken).ConfigureAwait(false);
        _gate.ThrowIfClosed();

        // Like the garbage collector: an image with a push going on keeps its loose layers (they wait for the manifest).
        var now = _time.UtcNow;
        var uploadCutoff = now - Options.UploadExpiry;
        if (await _context.OciUploads!.AsNoTracking().AnyAsync(u => u.ImageKey == row.Key && u.Updated >= uploadCutoff, cancellationToken).ConfigureAwait(false))
        {
            return new OciPruneResult(0, 0);
        }

        var cutoff = now - Options.PruneGrace;
        var candidates = await (from l in _context.OciBlobLinks!.AsNoTracking()
                                join b in _context.OciBlobs!.AsNoTracking() on l.BlobKey equals b.Key
                                where l.ImageKey == row.Key && l.Manifests == 0 && l.Created < cutoff
                                select new { l.Key, l.BlobKey, b.Size }).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (candidates.Count == 0) return new OciPruneResult(0, 0);

        // The plan is read again and deleted inside one serializable transaction, with the count in the delete's own condition.
        List<int> blobKeys;
        long bytes;
        int removed;
        await using (var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false))
        {
            var candidateKeys = candidates.Select(c => c.Key).ToList();
            var stillLoose = (await _context.OciBlobLinks!.AsNoTracking().Where(l => candidateKeys.Contains(l.Key) && l.Manifests == 0)
                .Select(l => l.Key).ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();
            var stale = candidates.Where(c => stillLoose.Contains(c.Key)).ToList();
            var staleKeys = stale.Select(s => s.Key).ToList();
            removed = staleKeys.Count == 0 ? 0 : await _context.OciBlobLinks!.Where(l => staleKeys.Contains(l.Key) && l.Manifests == 0).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            blobKeys = stale.Select(s => s.BlobKey).ToList();
            bytes = stale.Sum(s => s.Size);
        }

        if (removed == 0) return new OciPruneResult(0, 0);

        await ReleaseBlobsAsync(blobKeys, cancellationToken).ConfigureAwait(false);

        return new OciPruneResult(removed, bytes);
    }

    // ---- the pages --------------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<OciImagePage> ListImagesAsync(string? text, int skip, int take, Func<string, CancellationToken, Task<bool>>? canRead, CancellationToken cancellationToken)
    {
        IQueryable<OciRepository> query = _context.OciRepositories!.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(text))
        {
            var needle = text.Trim().ToLowerInvariant();
            query = query.Where(i => i.Name!.Contains(needle));
        }

        var all = await query.OrderBy(i => i.Name).Select(i => new { i.Key, i.Name, i.Modified }).ToListAsync(cancellationToken).ConfigureAwait(false);

        var visible = new List<(int Key, string Name, DateTime Modified)>();
        foreach (var image in all)
        {
            if (canRead == null || await canRead(image.Name!, cancellationToken).ConfigureAwait(false)) visible.Add((image.Key, image.Name!, image.Modified));
        }

        var page = visible.OrderBy(i => i.Name, StringComparer.Ordinal).Skip(Math.Max(skip, 0)).Take(Math.Clamp(take, 0, 250)).ToList();
        var keys = page.Select(p => p.Key).ToList();

        var tags = await _context.OciTags!.AsNoTracking().Where(t => keys.Contains(t.ImageKey)).GroupBy(t => t.ImageKey).Select(g => new { Key = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken).ConfigureAwait(false);
        var manifests = await _context.OciManifests!.AsNoTracking().Where(m => keys.Contains(m.ImageKey)).GroupBy(m => m.ImageKey).Select(g => new { Key = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken).ConfigureAwait(false);
        var sizes = await (from l in _context.OciBlobLinks!.AsNoTracking()
                           join b in _context.OciBlobs!.AsNoTracking() on l.BlobKey equals b.Key
                           where keys.Contains(l.ImageKey)
                           group b by l.ImageKey into g
                           select new { Key = g.Key, Size = g.Sum(b => b.Size) }).ToDictionaryAsync(g => g.Key, g => g.Size, cancellationToken).ConfigureAwait(false);

        return new OciImagePage(
            visible.Count,
            page.Select(p => new OciImageSummary(p.Name, tags.GetValueOrDefault(p.Key), manifests.GetValueOrDefault(p.Key), sizes.GetValueOrDefault(p.Key), Utc(p.Modified))).ToList());
    }

    /// <inheritdoc/>
    public async Task<OciImageInfo?> GetImageAsync(string image, CancellationToken cancellationToken)
    {
        if (!OciNames.IsValidImage(image)) return null;

        var row = await FindImageAsync(image, cancellationToken).ConfigureAwait(false);
        if (row == null) return null;

        var tagRows = await (from t in _context.OciTags!.AsNoTracking()
                             join m in _context.OciManifests!.AsNoTracking() on t.ManifestKey equals m.Key
                             where t.ImageKey == row.Key
                             select new { t.Name, m.Digest, t.Updated, t.ManifestKey }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var manifests = await _context.OciManifests!.AsNoTracking().Where(m => m.ImageKey == row.Key).OrderByDescending(m => m.Created).Take(1000).ToListAsync(cancellationToken).ConfigureAwait(false);

        // The platform of a child is known from the index that lists it.
        var platforms = new Dictionary<string, string>(StringComparer.Ordinal);
        var parsedByDigest = new Dictionary<string, OciParsedManifest?>(StringComparer.Ordinal);
        foreach (var manifest in manifests)
        {
            var parsed = TryParse(manifest);
            parsedByDigest[manifest.Digest!] = parsed;
            if (parsed?.IsIndex == true)
            {
                foreach (var child in parsed.Children)
                {
                    platforms.TryAdd(child.Descriptor.Digest, new OciPlatformInfo(child.Descriptor.Digest, child.Descriptor.MediaType, child.Descriptor.Size, child.Os, child.Architecture, child.Variant, true).Platform);
                }
            }
        }

        var tagsByManifest = tagRows.GroupBy(t => t.ManifestKey).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(t => t.Name!).OrderBy(n => n, StringComparer.Ordinal).ToList());
        var summaries = manifests.Select(m => Summarize(m, parsedByDigest[m.Digest!], tagsByManifest.GetValueOrDefault(m.Key) ?? Array.Empty<string>(), platforms.GetValueOrDefault(m.Digest!))).ToList();

        var size = await (from l in _context.OciBlobLinks!.AsNoTracking()
                          join b in _context.OciBlobs!.AsNoTracking() on l.BlobKey equals b.Key
                          where l.ImageKey == row.Key
                          select (long?)b.Size).SumAsync(cancellationToken).ConfigureAwait(false) ?? 0;
        var unreferenced = await _context.OciBlobLinks!.AsNoTracking().CountAsync(l => l.ImageKey == row.Key && l.Manifests == 0, cancellationToken).ConfigureAwait(false);

        return new OciImageInfo(
            row.Name!,
            Utc(row.Created),
            Utc(row.Modified),
            tagRows.OrderBy(t => t.Name, StringComparer.Ordinal).Select(t => new OciTagInfo(t.Name!, t.Digest!, Utc(t.Updated))).ToList(),
            summaries,
            size,
            unreferenced);
    }

    private static OciParsedManifest? TryParse(OciManifest manifest)
    {
        try
        {
            return OciManifestParser.Parse(manifest.MediaType, Encoding.UTF8.GetBytes(manifest.Content!));
        }
        catch (OciException)
        {
            return null;
        }
    }

    private static OciManifestSummary Summarize(OciManifest manifest, OciParsedManifest? parsed, IReadOnlyList<string> tags, string? platform)
        => new(
            manifest.Digest!,
            manifest.MediaType!,
            manifest.Size,
            OciNames.IsIndex(manifest.MediaType),
            Utc(manifest.Created),
            tags,
            manifest.ArtifactType,
            manifest.SubjectDigest,
            platform,
            parsed?.Layers.Count ?? 0,
            parsed == null ? 0 : parsed.Layers.Sum(l => l.Size) + (parsed.Config?.Size ?? 0));

    /// <inheritdoc/>
    public async Task<OciManifestDetail?> GetManifestDetailAsync(string image, string digest, CancellationToken cancellationToken)
    {
        if (!OciNames.IsValidImage(image) || !OciNames.IsValidDigest(digest)) return null;

        var row = await FindImageAsync(image, cancellationToken).ConfigureAwait(false);
        if (row == null) return null;

        var manifest = await _context.OciManifests!.AsNoTracking().FirstOrDefaultAsync(m => m.ImageKey == row.Key && m.Digest == digest, cancellationToken).ConfigureAwait(false);
        if (manifest == null) return null;

        var parsed = TryParse(manifest);
        var tags = await _context.OciTags!.AsNoTracking().Where(t => t.ManifestKey == manifest.Key).Select(t => t.Name!).ToListAsync(cancellationToken).ConfigureAwait(false);

        string? platform = null;
        var platforms = new List<OciPlatformInfo>();
        if (parsed != null && parsed.IsIndex)
        {
            var present = await _context.OciManifests!.AsNoTracking().Where(m => m.ImageKey == row.Key).Select(m => m.Digest!).ToListAsync(cancellationToken).ConfigureAwait(false);
            var set = present.ToHashSet(StringComparer.Ordinal);
            platforms.AddRange(parsed.Children.Select(c => new OciPlatformInfo(c.Descriptor.Digest, c.Descriptor.MediaType, c.Descriptor.Size, c.Os, c.Architecture, c.Variant, set.Contains(c.Descriptor.Digest))));
        }
        else if (parsed?.Config != null)
        {
            platform = await ReadPlatformAsync(row.Key, parsed.Config, cancellationToken).ConfigureAwait(false);
        }

        string json;
        try
        {
            using var document = JsonDocument.Parse(manifest.Content!);
            json = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }
        catch (JsonException)
        {
            json = manifest.Content!;
        }

        return new OciManifestDetail(
            Summarize(manifest, parsed, tags.OrderBy(t => t, StringComparer.Ordinal).ToList(), platform),
            json,
            parsed?.Config == null ? null : new OciLayerInfo(parsed.Config.Digest, parsed.Config.Size, parsed.Config.MediaType),
            parsed?.Layers.Select(l => new OciLayerInfo(l.Digest, l.Size, l.MediaType)).ToList() ?? new List<OciLayerInfo>(),
            platforms,
            parsed?.Annotations ?? new Dictionary<string, string>());
    }

    // The config blob of an image manifest says which platform it is for; small, so read whole (bounded).
    private async Task<string?> ReadPlatformAsync(int imageKey, OciParsedDescriptor config, CancellationToken cancellationToken)
    {
        if (config.Size is <= 0 or > 1024 * 1024) return null;

        try
        {
            var blob = await FindHeldBlobAsync(imageKey, config.Digest, cancellationToken).ConfigureAwait(false);
            if (blob == null) return null;

            using var stream = await Streaming.GetRangeAsync(BlobPath(config.Digest), 0, blob.Size, cancellationToken).ConfigureAwait(false);
            if (stream == null) return null;

            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            string? Get(string name) => document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var parts = new[] { Get("os"), Get("architecture"), Get("variant") }.Where(p => !string.IsNullOrEmpty(p)).ToArray();

            return parts.Length == 0 ? null : string.Join("/", parts!);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NotSupportedException or IOException)
        {
            return null;
        }
    }
}
