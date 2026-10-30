using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaGet.Core;

/// <summary>What a garbage collection found (or, outside a dry run, removed).</summary>
public enum OciGcItemKind
{
    /// <summary>An upload session nobody touched for <see cref="OciOptions.UploadExpiry"/>: the session and what it received.</summary>
    ExpiredUpload,

    /// <summary>A layer an image holds that none of the image's manifests uses and that is older than <see cref="OciOptions.PruneGrace"/>.</summary>
    UnreferencedLayer,

    /// <summary>A blob row that no image holds any more (its file goes with it).</summary>
    OrphanBlob,

    /// <summary>A file under the blob folder that no blob row names (a push that died between storing the file and recording it).</summary>
    OrphanFile,
}

/// <summary>One thing a garbage collection removes.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Subject">A digest or a session reference.</param>
/// <param name="Bytes">Bytes it frees (0 for a session).</param>
/// <param name="Image">The image it belonged to, if any.</param>
public sealed record OciGcItem(OciGcItemKind Kind, string Subject, long Bytes, string? Image);

/// <summary>The outcome of one garbage collection of a repository.</summary>
/// <param name="DryRun">Whether nothing was changed (the numbers are what a run would remove).</param>
/// <param name="Repository">The repository.</param>
/// <param name="ExpiredUploads">Upload sessions discarded.</param>
/// <param name="UnreferencedLayers">Image-to-layer links dropped (a layer two images hold counts twice).</param>
/// <param name="Blobs">Blobs (rows and files) removed: those no image holds after the links are dropped, orphan rows included.</param>
/// <param name="OrphanFiles">Files under the blob folder with no row, removed.</param>
/// <param name="Bytes">Bytes freed by the blobs and orphan files, as far as they are known (an orphan file's size is not).</param>
/// <param name="Items">The first of the things removed (the list is capped).</param>
/// <param name="ItemsTruncated">Whether there are more than <paramref name="Items"/> holds.</param>
/// <param name="Note">Something the run could not do (the store cannot list files), or null.</param>
public sealed record OciGcReport(
    bool DryRun,
    string Repository,
    int ExpiredUploads,
    int UnreferencedLayers,
    int Blobs,
    int OrphanFiles,
    long Bytes,
    IReadOnlyList<OciGcItem> Items,
    bool ItemsTruncated,
    string? Note)
{
    /// <summary>One line for the Operations page and the audit log.</summary>
    public string Summary
    {
        get
        {
            var text = $"{(DryRun ? "Dry run, nothing removed. Would remove" : "Removed")}: {ExpiredUploads} expired upload session(s), "
                       + $"{UnreferencedLayers} unreferenced layer link(s), {Blobs} blob(s) ({FormatBytes(Bytes)}), {OrphanFiles} orphan file(s).";
            if (Note != null) text += " " + Note;

            return text;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }
}

/// <summary>
/// Removes the blobs of a Docker repository (local or remote) that nothing uses, in the repository of the request.
/// </summary>
public interface IOciGarbageCollector
{
    /// <summary>
    /// Finds what is unreferenced and, unless <paramref name="dryRun"/>, removes it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The repository is not a Docker repository with blobs of its own (a virtual one has none).</exception>
    /// <exception cref="MaintenanceException">Not a dry run and the server is read-only while a database copy runs.</exception>
    Task<OciGcReport> RunAsync(bool dryRun, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IOciGarbageCollector"/>: mark and sweep over the repository's rows and files.
/// </summary>
/// <remarks>
/// <para><b>Marked</b>, in this order: (1) upload sessions idle longer than <see cref="OciOptions.UploadExpiry"/>; (2) image-to-layer links
/// no manifest of the image uses (<see cref="OciBlobLink.Manifests"/> is 0) and older than <see cref="OciOptions.PruneGrace"/>, <b>except
/// those of an image with an upload session still open</b> (a push in progress sends layers first and the manifest last, however long the
/// layers take); (3) blobs no image holds once those links are gone (reference count 0), and blob rows nobody ever linked, older than the
/// grace period; (4) files under the blob folder that no blob row names, when the store can list files and no blob is being committed
/// right now (a file is stored a moment before its row is written).</para>
/// <para><b>Swept</b> (not a dry run): the links and blobs are first re-checked against the current rows in a serializable
/// transaction (a manifest or push that committed since the plan was made keeps its links and blobs; the report counts only what was
/// removed), then the sessions, links and blob rows are deleted, the sessions aborted in storage, then the files. A file that cannot be deleted is logged and counted; the next run finds it again as an orphan file.</para>
/// <para>This is what <c>Oci.PruneGrace</c> and <c>Oci.UploadExpiry</c> are for; running it for every repository on a schedule is the
/// retention engine's job (P6), the Operations page runs it by hand.</para>
/// </remarks>
public sealed class OciGarbageCollector : IOciGarbageCollector
{
    /// <summary>The most items a report lists.</summary>
    public const int MaxItems = 500;

    private readonly IContext _context;
    private readonly IStorageService _storage;
    private readonly IRepositoryContext _repository;
    private readonly SystemTime _time;
    private readonly MaintenanceGate _gate;
    private readonly ILogger<OciGarbageCollector> _logger;
    private readonly IOptionsSnapshot<OciOptions>? _options;
    private readonly ClusterRuntime? _cluster;

    public OciGarbageCollector(
        IContext context,
        IStorageService storage,
        IRepositoryContext repository,
        SystemTime time,
        MaintenanceGate gate,
        ILogger<OciGarbageCollector> logger,
        IOptionsSnapshot<OciOptions>? options = null,
        ClusterRuntime? cluster = null)
    {
        _cluster = cluster;
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

    /// <summary>Test seam: runs after the blob rows were loaded and before the storage listing, with the repository name.</summary>
    internal static Func<string, Task>? AfterRowsLoaded { get; set; }

    private static string BlobFolder => OciNames.StorageFolder + "/blobs";

    /// <inheritdoc/>
    public async Task<OciGcReport> RunAsync(bool dryRun, CancellationToken cancellationToken)
    {
        var repository = _repository.Current;
        if (repository.Format != RepositoryFormat.Docker || repository.Kind == RepositoryKind.Virtual || _context is NullContext)
        {
            throw new InvalidOperationException($"'{repository.Name}' is not a Docker repository with blobs of its own.");
        }

        if (!dryRun) _gate.ThrowIfClosed();

        var now = _time.UtcNow;
        var uploadCutoff = now - Options.UploadExpiry;
        var layerCutoff = now - Options.PruneGrace;
        var items = new List<OciGcItem>();
        var truncated = false;

        void Add(OciGcItem item)
        {
            if (items.Count < MaxItems) items.Add(item);
            else truncated = true;
        }

        // (1) Sessions nobody touched for a day. The image join keeps this to the repository of the request.
        var expired = await (from u in _context.OciUploads!
                             join i in _context.OciRepositories! on u.ImageKey equals i.Key
                             where u.Updated < uploadCutoff
                             select new { Session = u, Image = i.Name }).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var e in expired) Add(new OciGcItem(OciGcItemKind.ExpiredUpload, e.Session.Reference!, 0, e.Image));

        // Images with a push that is still going on: their loose layers wait for the manifest.
        var pushing = (await (from u in _context.OciUploads!.AsNoTracking()
                              join i in _context.OciRepositories!.AsNoTracking() on u.ImageKey equals i.Key
                              where u.Updated >= uploadCutoff
                              select u.ImageKey).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();

        // (2) Layers an image holds that none of its manifests uses.
        var loose = (await (from l in _context.OciBlobLinks!
                            join b in _context.OciBlobs! on l.BlobKey equals b.Key
                            join i in _context.OciRepositories! on l.ImageKey equals i.Key
                            where l.Manifests == 0 && l.Created < layerCutoff
                            select new { Link = l, b.Digest, b.Size, Image = i.Name }).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Where(l => !pushing.Contains(l.Link.ImageKey))
            .ToList();
        foreach (var l in loose) Add(new OciGcItem(OciGcItemKind.UnreferencedLayer, l.Digest!, l.Size, l.Image));

        // (3) Blobs nothing holds once those links are gone.
        var linkCounts = await (from l in _context.OciBlobLinks!.AsNoTracking()
                                join b in _context.OciBlobs!.AsNoTracking() on l.BlobKey equals b.Key
                                group l by l.BlobKey into g
                                select new { BlobKey = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.BlobKey, g => g.Count, cancellationToken).ConfigureAwait(false);
        var droppedPerBlob = loose.GroupBy(l => l.Link.BlobKey).ToDictionary(g => g.Key, g => g.Count());

        var blobs = await _context.OciBlobs!.ToListAsync(cancellationToken).ConfigureAwait(false);
        var doomed = new List<OciBlob>();
        foreach (var blob in blobs)
        {
            var held = linkCounts.GetValueOrDefault(blob.Key);
            var dropped = droppedPerBlob.GetValueOrDefault(blob.Key);
            if (held == 0 && blob.Created >= layerCutoff) continue;
            if (held - dropped > 0) continue;

            doomed.Add(blob);
            if (held == 0) Add(new OciGcItem(OciGcItemKind.OrphanBlob, blob.Digest!, blob.Size, null));
        }

        var doomedKeys = doomed.Select(b => b.Key).ToHashSet();
        var doomedDigests = doomed.Select(b => b.Digest!).ToHashSet(StringComparer.Ordinal);
        var bytes = doomed.Sum(b => b.Size);

        // (4) Files no row names.
        var survivors = blobs.Where(b => !doomedKeys.Contains(b.Key)).Select(b => b.Digest!).ToHashSet(StringComparer.Ordinal);
        var orphanFiles = new List<string>();
        string? note = null;
        if (_cluster?.IsEnabled == true)
        {
            // The in-process commit fence below cannot see a blob another node is storing, so a listing could take a file whose row is
            // about to be written. In a cluster the step is skipped rather than risk a layer.
            note = "Files without a row were not looked for: other nodes of the cluster may be storing blobs, which this node cannot see.";
        }
        else if (_storage is not IListableStorageService listable)
        {
            note = "The storage provider cannot list files, so files without a row were not looked for.";
        }
        else
        {
            // A file is an orphan only if no row names it AFTER the listing: a blob committed since `blobs` was loaded has a row now.
            // Commits of this process are fenced: none may start during the listing nor be running when it ends (a commit writes its
            // row before it leaves the counter), so every file the listing shows has its row by the time the rows are read again.
            // Other nodes sharing the store are not fenced (see docs/docker-registry.md).
            var folder = RepositoryStorage.Scope(repository, BlobFolder);
            if (AfterRowsLoaded != null) await AfterRowsLoaded(repository.Name).ConfigureAwait(false);
            var startedBefore = OciBlobCommits.Started;
            var listed = await listable.ListAsync(folder, cancellationToken).ConfigureAwait(false);
            if (OciBlobCommits.InFlight > 0 || OciBlobCommits.Started != startedBefore)
            {
                note = "A blob was being stored while this ran, so files without a row were not looked for; run it again.";
            }
            else
            {
                var current = (await _context.OciBlobs!.AsNoTracking().Select(b => b.Digest!).ToListAsync(cancellationToken).ConfigureAwait(false))
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var path in listed)
                {
                    var digest = DigestOfPath(path, folder);
                    if (digest == null || survivors.Contains(digest) || doomedDigests.Contains(digest) || current.Contains(digest)) continue;

                    orphanFiles.Add(path);
                    Add(new OciGcItem(OciGcItemKind.OrphanFile, digest, 0, null));
                }
            }
        }

        var removedLinks = loose.Count;
        var removedBlobs = doomed.Count;
        if (!dryRun)
        {
            var swept = await SweepAsync(repository, expired.Select(e => e.Session).ToList(), loose.Select(l => l.Link).ToList(), doomed, orphanFiles, cancellationToken).ConfigureAwait(false);
            removedLinks = swept.Links;
            removedBlobs = swept.Blobs;
            bytes = swept.Bytes;
            if (swept.Kept > 0)
            {
                var kept = $"{swept.Kept} layer link(s) or blob(s) were used by a push while this ran and were kept.";
                note = note == null ? kept : note + " " + kept;
            }
        }

        var report = new OciGcReport(dryRun, repository.Name, expired.Count, removedLinks, removedBlobs, orphanFiles.Count, bytes, items, truncated, note);
        _logger.LogInformation("Docker garbage collection of {Repository}{DryRun}: {Summary}", repository.Name, dryRun ? " (dry run)" : string.Empty, report.Summary);

        return report;
    }

    // "<folder>/sha256/ab/<hex>" -> "sha256:<hex>"; anything else under the folder is not a blob this registry wrote.
    private static string? DigestOfPath(string path, string folder)
    {
        var relative = path.Replace('\\', '/');
        var start = relative.IndexOf(folder + "/", StringComparison.Ordinal);
        if (start < 0) return null;

        var parts = relative.Substring(start + folder.Length + 1).Split('/');
        if (parts.Length != 3 || parts[0] != OciNames.Sha256) return null;

        var hex = parts[2];

        return StreamingStorage.IsSha256Hex(hex) && parts[1] == hex.Substring(0, 2) ? OciNames.DigestOf(hex) : null;
    }

    // What the sweep really removed (the plan may shrink: see SweepAsync).
    private readonly record struct Swept(int Links, int Blobs, long Bytes, int Kept);

    private async Task<Swept> SweepAsync(
        RepositoryInfo repository,
        IReadOnlyList<OciUpload> expired,
        IReadOnlyList<OciBlobLink> loose,
        IReadOnlyList<OciBlob> doomed,
        IReadOnlyList<string> orphanFiles,
        CancellationToken cancellationToken)
    {
        // The plan was made from rows read earlier; a manifest committed since then has raised Manifests of a planned link (or a push
        // has linked a planned blob to another image). Deleting by key would remove that now-used link and its blob. So the plan is
        // checked again inside a serializable transaction, against the rows as they are now, and shrinks to what is still unused;
        // a committer running at the same time waits for the transaction or fails and retries on its own.
        IReadOnlyList<OciBlobLink> removableLinks = loose;
        IReadOnlyList<OciBlob> removableBlobs = doomed;
        var kept = 0;

        // An upload of a planned blob in this process either records its row before the transaction below (the blob is kept) or stores
        // its file after the deletion: the digests stay locked from the check to the file deletion (OciBlobCommits).
        using var blobLocks = await OciBlobCommits.LockAsync(doomed.Select(b => RepositoryStorage.Scope(repository, OciNames.BlobPath(b.Digest!))), cancellationToken).ConfigureAwait(false);
        if (loose.Count > 0 || doomed.Count > 0)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);

            var looseKeys = loose.Select(l => l.Key).ToList();
            var stillLoose = (await _context.OciBlobLinks!.AsNoTracking().Where(l => looseKeys.Contains(l.Key) && l.Manifests == 0)
                .Select(l => l.Key).ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();
            removableLinks = loose.Where(l => stillLoose.Contains(l.Key)).ToList();

            var doomedKeys = doomed.Select(b => b.Key).ToList();
            var linkCounts = await _context.OciBlobLinks!.AsNoTracking().Where(l => doomedKeys.Contains(l.BlobKey))
                .GroupBy(l => l.BlobKey).Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken).ConfigureAwait(false);
            var droppedPerBlob = removableLinks.GroupBy(l => l.BlobKey).ToDictionary(g => g.Key, g => g.Count());
            removableBlobs = doomed.Where(b => linkCounts.GetValueOrDefault(b.Key) - droppedPerBlob.GetValueOrDefault(b.Key) <= 0).ToList();
            kept = (loose.Count - removableLinks.Count) + (doomed.Count - removableBlobs.Count);

            if (expired.Count > 0) _context.OciUploads!.RemoveRange(expired);
            if (removableLinks.Count > 0) _context.OciBlobLinks!.RemoveRange(removableLinks);
            if (removableBlobs.Count > 0) _context.OciBlobs!.RemoveRange(removableBlobs);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (expired.Count > 0)
        {
            _context.OciUploads!.RemoveRange(expired);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var session in expired)
        {
            try
            {
                await Streaming.AbortAsync(session.StorageUploadId!, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger.LogWarning(e, "Could not abort the upload {UploadId}", session.StorageUploadId);
            }
        }

        foreach (var path in removableBlobs.Select(b => RepositoryStorage.Scope(repository, OciNames.BlobPath(b.Digest!))).Concat(orphanFiles))
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

        return new Swept(removableLinks.Count, removableBlobs.Count, removableBlobs.Sum(b => b.Size), kept);
    }
}

/// <summary>The garbage collection as an <see cref="IAdminOperation"/> (a dry run or the real thing), run from the Operations page.</summary>
public abstract class OciGarbageCollectionOperation : IAdminOperation
{
    private readonly bool _dryRun;

    /// <param name="dryRun">Whether the operation only reports.</param>
    protected OciGarbageCollectionOperation(bool dryRun)
    {
        _dryRun = dryRun;
    }

    /// <summary>The name of the operation that only reports.</summary>
    public const string DryRunName = "oci-gc-dry-run";

    /// <summary>The name of the operation that removes.</summary>
    public const string RunName = "oci-gc";

    /// <inheritdoc/>
    public string Name => _dryRun ? DryRunName : RunName;

    /// <inheritdoc/>
    public string Title => _dryRun ? "Docker garbage collection: dry run" : "Docker garbage collection";

    /// <inheritdoc/>
    public string Description => _dryRun
        ? "Reports the Docker blobs, layer links, upload sessions and orphan files that a collection would remove from the repository, and removes nothing."
        : "Removes the Docker blobs nothing references (unused layers past the grace period, expired upload sessions, orphan blobs and files) from the repository.";

    /// <inheritdoc/>
    public bool IsAvailable(IServiceProvider services)
        => services.GetRequiredService<IContext>() is not NullContext && StreamingStorage.IsSupported(services.GetRequiredService<IStorageService>());

    /// <inheritdoc/>
    public bool AppliesTo(RepositoryInfo repository)
        => repository.Format == RepositoryFormat.Docker && repository.Kind != RepositoryKind.Virtual;

    /// <inheritdoc/>
    /// <remarks>The collection that removes asks for the password; the dry run deletes nothing.</remarks>
    public bool RequiresConfirmation => !_dryRun;

    /// <inheritdoc/>
    public async Task<string> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var report = await services.GetRequiredService<IOciGarbageCollector>().RunAsync(_dryRun, cancellationToken).ConfigureAwait(false);
        var listed = string.Join("; ", report.Items.Take(10).Select(i => $"{i.Kind} {i.Subject}"));

        return report.Summary + (listed.Length == 0 ? string.Empty : " First: " + listed + (report.Items.Count > 10 || report.ItemsTruncated ? "; ..." : string.Empty));
    }
}

/// <summary>The garbage collection that only reports (<see cref="OciGarbageCollectionOperation.DryRunName"/>).</summary>
public sealed class OciGarbageCollectionDryRunOperation : OciGarbageCollectionOperation
{
    public OciGarbageCollectionDryRunOperation()
        : base(dryRun: true)
    {
    }
}

/// <summary>The garbage collection that removes (<see cref="OciGarbageCollectionOperation.RunName"/>).</summary>
public sealed class OciGarbageCollectionRunOperation : OciGarbageCollectionOperation
{
    public OciGarbageCollectionRunOperation()
        : base(dryRun: false)
    {
    }
}
