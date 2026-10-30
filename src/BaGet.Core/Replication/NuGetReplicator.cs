using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NuGet.Versioning;

namespace BaGet.Core;

/// <summary>
/// NuGet's <see cref="IFormatReplicator"/>. Items are package versions (id compared without regard to case, version normalized). A push
/// is the protocol's <c>PUT api/v2/package</c> with the peer's key in <c>X-NuGet-ApiKey</c>; a pull downloads the flat-container
/// <c>.nupkg</c> and submits it through the same pipeline a push uses (validation included). NuGet keeps no content digest, so two
/// servers holding the same id and version are taken to hold the same package (versions are immutable) and a conflict cannot be detected.
/// Deleting at the peer is the protocol's <c>DELETE api/v2/package/{id}/{version}</c>, which honours the peer's
/// <c>PackageDeletionBehavior</c> (an unlist leaves the version there).
/// </summary>
public sealed class NuGetReplicator : IFormatReplicator
{
    private readonly IContext _context;
    private readonly IPackageStorageService _storage;
    private readonly IPackageSubmissionService _submissions;
    private readonly IPackageDeletionService _deletion;
    private readonly MaintenanceGate _gate;
    private readonly IScanGate _scanGate;

    public NuGetReplicator(
        IContext context,
        IPackageStorageService storage,
        IPackageSubmissionService submissions,
        IPackageDeletionService deletion,
        MaintenanceGate gate,
        IScanGate scanGate)
    {
        _scanGate = scanGate ?? throw new ArgumentNullException(nameof(scanGate));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _submissions = submissions ?? throw new ArgumentNullException(nameof(submissions));
        _deletion = deletion ?? throw new ArgumentNullException(nameof(deletion));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    /// <inheritdoc/>
    public RepositoryFormat Format => RepositoryFormat.NuGet;

    /// <inheritdoc/>
    public StringComparer NameComparer => StringComparer.OrdinalIgnoreCase;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ReplicaItem>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await _context.Packages!.AsNoTracking()
            .Select(p => new { p.Id, p.NormalizedVersionString })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows
            .Select(r => new ReplicaItem(r.Id!, r.NormalizedVersionString!, string.Empty, 0, string.Empty))
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Version, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<long> PushAsync(ReplicaItem item, ReplicationPeer peer, CancellationToken cancellationToken)
    {
        using var stored = await _storage.GetPackageStreamAsync(item.Name, NuGetVersion.Parse(item.Version), cancellationToken).ConfigureAwait(false)
            ?? throw new ReplicationItemException($"The package {item} is listed but its file is not in the store.");
        using var file = await ReplicationTransfer.SpoolAsync(stored, cancellationToken).ConfigureAwait(false);
        var length = file.Length;

        var request = new HttpRequestMessage(HttpMethod.Put, peer.Feed("api/v2/package")) { Content = ReplicationTransfer.Binary(file) };
        peer.AddCredentials(request);
        await ReplicationTransfer.SendAsync(peer, request, "push", item, cancellationToken).ConfigureAwait(false);

        return length;
    }

    /// <inheritdoc/>
    public Task<long> PullAsync(ReplicaItem item, ReplicationPeer peer, CancellationToken cancellationToken)
        => PullCoreAsync(item, peer, replace: false, cancellationToken);

    /// <summary>Downloads and judges the package (scanning policy) first; only then removes the local copy and submits the peer's.</summary>
    public Task<long> PullReplacingAsync(ReplicaItem item, ReplicationPeer peer, CancellationToken cancellationToken)
        => PullCoreAsync(item, peer, replace: true, cancellationToken);

    private async Task<long> PullCoreAsync(ReplicaItem item, ReplicationPeer peer, bool replace, CancellationToken cancellationToken)
    {
        _gate.ThrowIfClosed();

        var id = item.Name.ToLowerInvariant();
        var version = item.Version.ToLowerInvariant();
        using var response = await ReplicationTransfer.GetAsync(peer, peer.Feed($"v3/package/{Uri.EscapeDataString(id)}/{Uri.EscapeDataString(version)}/{Uri.EscapeDataString(id)}.{Uri.EscapeDataString(version)}.nupkg"), item, cancellationToken).ConfigureAwait(false);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var file = await ReplicationTransfer.SpoolAsync(body, cancellationToken).ConfigureAwait(false);
        var length = file.Length;

        // A push checks the scanning policy before it stores; a pull must refuse the same packages.
        if (_scanGate.Enabled && await _scanGate.BlocksDeploysAsync(cancellationToken).ConfigureAwait(false))
        {
            var components = NuGetScanComponents.TryRead(file);
            if (components != null)
            {
                try
                {
                    await _scanGate.CheckDeployAsync(components, cancellationToken).ConfigureAwait(false);
                }
                catch (ScanBlockedException e)
                {
                    throw new ReplicationItemException($"The scanning policy refused {item}: {e.Message}", inner: e);
                }
            }
        }

        if (replace) await DeleteLocalAsync(item, cancellationToken).ConfigureAwait(false);

        var result = await _submissions.SubmitAsync(file, cancellationToken).ConfigureAwait(false);
        switch (result)
        {
            case PackageIndexingResult.Success:
            case PackageIndexingResult.Accepted:
                return length;

            case PackageIndexingResult.PackageAlreadyExists:
                throw new ReplicationItemException($"The package {item} exists already.", conflict: true);

            default:
                throw new ReplicationItemException($"The package {item} from the peer is not a valid package.");
        }
    }

    /// <inheritdoc/>
    public async Task DeleteLocalAsync(ReplicaItem item, CancellationToken cancellationToken)
        => await _deletion.TryHardDeletePackageAsync(item.Name, NuGetVersion.Parse(item.Version), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task DeletePeerAsync(ReplicaItem item, ReplicationPeer peer, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, peer.Feed($"api/v2/package/{Uri.EscapeDataString(item.Name)}/{Uri.EscapeDataString(item.Version)}"));
        peer.AddCredentials(request);
        await ReplicationTransfer.SendAsync(peer, request, "delete", item, cancellationToken, allowNotFound: true).ConfigureAwait(false);
    }
}
