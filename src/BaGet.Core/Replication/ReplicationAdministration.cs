using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BaGet.Core;

/// <summary>What the administrator types to create or change a replication. <see cref="Key"/> 0 creates.</summary>
/// <param name="Key">The configuration, or 0.</param>
/// <param name="Name">Its unique name.</param>
/// <param name="Repository">The local repository.</param>
/// <param name="Direction">Push or pull.</param>
/// <param name="PeerUrl">The peer server's base address.</param>
/// <param name="PeerRepository">The peer's repository (<c>default</c> or its name).</param>
/// <param name="ApiKey">The peer's API key. Null or empty keeps the stored one (changing the peer's address drops it).</param>
/// <param name="ClearApiKey">Forget the stored key (an anonymous peer).</param>
/// <param name="Enabled">Whether runs are allowed.</param>
/// <param name="OnDeploy">Push only: start a run after a deploy to the local repository.</param>
/// <param name="ScheduleMinutes">Run every this many minutes, or null.</param>
/// <param name="PropagateDeletes">Delete at the target what was copied and is deleted at the source.</param>
/// <param name="Conflict">What happens when both sides hold the item with different content.</param>
public sealed record ReplicationInput(
    int Key,
    string? Name,
    string? Repository,
    ReplicationDirection Direction,
    string? PeerUrl,
    string? PeerRepository,
    string? ApiKey,
    bool ClearApiKey,
    bool Enabled,
    bool OnDeploy,
    int? ScheduleMinutes,
    bool PropagateDeletes,
    ReplicationConflict Conflict);

/// <summary>A configuration with the names the page shows; the API key is never part of it.</summary>
/// <param name="Config">The configuration (its <see cref="ReplicationConfig.ProtectedApiKey"/> must not be shown).</param>
/// <param name="Repository">The local repository's name, or null when it no longer exists.</param>
/// <param name="Format">Its format.</param>
/// <param name="HasApiKey">Whether a key is stored.</param>
/// <param name="LastRun">The newest log entry, or null.</param>
public sealed record ReplicationConfigView(ReplicationConfig Config, string? Repository, RepositoryFormat? Format, bool HasApiKey, ReplicationRun? LastRun);

/// <summary>What <see cref="IReplicationAdministration.TestAsync"/> found.</summary>
/// <param name="Ok">Whether the peer answered as a BaGet server with this repository.</param>
/// <param name="Message">A sentence for the administrator (never the key).</param>
/// <param name="ElapsedMilliseconds">How long it took.</param>
/// <param name="PeerFormat">The peer repository's format, when it answered.</param>
/// <param name="PeerItems">How many items it holds, when it answered.</param>
public sealed record ReplicationTestResult(bool Ok, string Message, long ElapsedMilliseconds, RepositoryFormat? PeerFormat, int? PeerItems);

/// <summary>
/// Manages replication configurations and reads their log. Every change calls <see cref="MaintenanceGate.ThrowIfClosed"/> and is
/// audited (<see cref="AccessAuditEntry"/>, in the same save). Authorization is the caller's (the administrator pages and API). Starting a
/// run is <see cref="ReplicationJobs"/>.
/// </summary>
public interface IReplicationAdministration
{
    /// <summary>Every configuration, by name.</summary>
    Task<IReadOnlyList<ReplicationConfigView>> ListAsync(CancellationToken cancellationToken);

    /// <summary>One configuration, or null.</summary>
    Task<ReplicationConfigView?> GetAsync(int key, CancellationToken cancellationToken);

    /// <summary>Creates (<see cref="ReplicationInput.Key"/> 0) or changes a configuration.</summary>
    /// <exception cref="ReplicationException">The input is not valid.</exception>
    Task<ReplicationConfig> SaveAsync(ReplicationInput input, string? actor, CancellationToken cancellationToken);

    /// <summary>Deletes a configuration with its log and ledger (what it copied stays).</summary>
    /// <exception cref="ReplicationException">A run is going.</exception>
    Task<bool> DeleteAsync(int key, string? actor, CancellationToken cancellationToken);

    /// <summary>The log of a configuration, newest first.</summary>
    Task<IReadOnlyList<ReplicationRun>> ListRunsAsync(int configKey, int take, CancellationToken cancellationToken);

    /// <summary>One log entry, or null.</summary>
    Task<ReplicationRun?> GetRunAsync(int runKey, CancellationToken cancellationToken);

    /// <summary>
    /// Asks the peer for its inventory summary with the typed (or, for a stored configuration, saved) settings; saves nothing. A stored key
    /// is sent only while the peer's origin is unchanged.
    /// </summary>
    Task<ReplicationTestResult> TestAsync(ReplicationInput input, CancellationToken cancellationToken);
}

/// <summary><see cref="IReplicationAdministration"/> over the relational database.</summary>
public sealed class ReplicationAdministration : IReplicationAdministration
{
    private readonly IContext _context;
    private readonly IServiceProvider _services;
    private readonly IReplicationPeerFactory _peers;
    private readonly RemoteSettingsCodec _codec;
    private readonly SystemTime _time;
    private readonly MaintenanceGate _gate;
    private readonly ReplicationJobs? _jobs;
    private readonly IOptions<RemoteOptions>? _remoteOptions;

    public ReplicationAdministration(
        IContext context,
        IServiceProvider services,
        IReplicationPeerFactory peers,
        RemoteSettingsCodec codec,
        SystemTime time,
        MaintenanceGate gate,
        ReplicationJobs? jobs = null,
        IOptions<RemoteOptions>? remoteOptions = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _peers = peers ?? throw new ArgumentNullException(nameof(peers));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _jobs = jobs;
        _remoteOptions = remoteOptions;
    }

    private IContext Db => _context is NullContext ? throw new ReplicationException("Replication needs a relational database.") : _context;

    private bool BlockPrivate => _remoteOptions?.Value?.BlockPrivateAddresses == true;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ReplicationConfigView>> ListAsync(CancellationToken cancellationToken)
    {
        var configs = await Db.ReplicationConfigs!.AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken).ConfigureAwait(false);

        return await ViewsAsync(configs, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ReplicationConfigView?> GetAsync(int key, CancellationToken cancellationToken)
    {
        var config = await Db.ReplicationConfigs!.AsNoTracking().FirstOrDefaultAsync(c => c.Key == key, cancellationToken).ConfigureAwait(false);

        return config == null ? null : (await ViewsAsync(new[] { config }, cancellationToken).ConfigureAwait(false)).Single();
    }

    private async Task<IReadOnlyList<ReplicationConfigView>> ViewsAsync(IReadOnlyList<ReplicationConfig> configs, CancellationToken cancellationToken)
    {
        var repositories = await Db.Repositories!.AsNoTracking().Select(r => new { r.Key, r.Name, r.Format }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var keys = configs.Select(c => c.Key).ToList();
        var runs = await Db.ReplicationRuns!.AsNoTracking().Where(r => keys.Contains(r.ConfigKey)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var last = runs.GroupBy(r => r.ConfigKey).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Started).ThenByDescending(r => r.Key).First());

        return configs.Select(c =>
        {
            var repository = repositories.FirstOrDefault(r => r.Key == c.RepositoryKey);

            return new ReplicationConfigView(Redact(c), repository?.Name, repository?.Format, !string.IsNullOrEmpty(c.ProtectedApiKey), last.TryGetValue(c.Key, out var run) ? run : null);
        }).ToList();
    }

    // A copy without the protected key: a view must never carry it to a page or the API.
    private static ReplicationConfig Redact(ReplicationConfig c) => new()
    {
        Key = c.Key,
        Name = c.Name,
        RepositoryKey = c.RepositoryKey,
        Direction = c.Direction,
        PeerUrl = c.PeerUrl,
        PeerRepository = c.PeerRepository,
        ProtectedApiKey = null,
        Enabled = c.Enabled,
        OnDeploy = c.OnDeploy,
        ScheduleMinutes = c.ScheduleMinutes,
        PropagateDeletes = c.PropagateDeletes,
        Conflict = c.Conflict,
        ScheduleStart = c.ScheduleStart,
        LastRun = c.LastRun,
        LastResult = c.LastResult,
        Created = c.Created,
        Modified = c.Modified,
    };

    /// <inheritdoc/>
    public async Task<ReplicationConfig> SaveAsync(ReplicationInput input, string? actor, CancellationToken cancellationToken)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        var db = Db;
        _gate.ThrowIfClosed();

        var name = input.Name?.Trim();
        if (!AccessNames.IsValid(name, out var nameError)) throw new ReplicationException(nameError ?? "The name is not valid.");

        var repository = await db.Repositories!.AsNoTracking().FirstOrDefaultAsync(r => r.Name == input.Repository!.Trim(), cancellationToken).ConfigureAwait(false)
            ?? throw new ReplicationException($"There is no repository '{input.Repository?.Trim()}'.");
        if (repository.Kind != RepositoryKind.Local) throw new ReplicationException("Only a local repository is replicated: a remote one caches an upstream and a virtual one stores nothing.");
        if (_services.GetFormatService<IFormatReplicator>(repository.Format) == null) throw new ReplicationException($"Replication is not available for {repository.Format} repositories.");

        var peerUrl = ValidatePeer(input.PeerUrl, input.PeerRepository, out var peerRepository);
        if (!Enum.IsDefined(input.Direction)) throw new ReplicationException("Unknown direction.");
        if (!Enum.IsDefined(input.Conflict)) throw new ReplicationException("Unknown conflict rule.");
        if (input.OnDeploy && input.Direction != ReplicationDirection.Push) throw new ReplicationException("Only a push starts after a deploy.");
        if (input.ScheduleMinutes is int minutes && (minutes < 1 || minutes > 525_600)) throw new ReplicationException("Repeat every (minutes) must be between 1 and 525600.");
        if (input.ApiKey != null && input.ApiKey.Length > 512) throw new ReplicationException("The API key is too long.");

        // Compared in memory, case-insensitively, whatever the engine's collation.
        var names = await db.ReplicationConfigs!.AsNoTracking().Select(c => new { c.Key, c.Name }).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (names.Any(c => c.Key != input.Key && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))) throw new ReplicationException($"There is already a replication named '{name}'.");

        var now = _time.UtcNow;
        ReplicationConfig row;
        string action;
        if (input.Key == 0)
        {
            row = new ReplicationConfig { Created = now };
            db.ReplicationConfigs!.Add(row);
            action = "replication.create";
        }
        else
        {
            row = await db.ReplicationConfigs!.FirstOrDefaultAsync(c => c.Key == input.Key, cancellationToken).ConfigureAwait(false)
                ?? throw new ReplicationException("The replication no longer exists.");
            if (_jobs?.IsRunning(row.Key) == true) throw new ReplicationException("The replication is running; stop it before changing it.");

            action = "replication.update";
        }

        var originChanged = input.Key != 0 && !SameOrigin(row.PeerUrl, peerUrl);
        var targetChanged = input.Key != 0
            && (row.RepositoryKey != repository.Key || row.Direction != input.Direction || originChanged
                || !string.Equals(row.PeerRepository, peerRepository, StringComparison.Ordinal));

        // The ledger says what *this* relationship copied; a different repository, direction or peer makes it meaningless.
        if (targetChanged)
        {
            db.ReplicationItems!.RemoveRange(await db.ReplicationItems!.Where(i => i.ConfigKey == row.Key).ToListAsync(cancellationToken).ConfigureAwait(false));
        }

        var keyChanged = false;
        if (input.ClearApiKey || (originChanged && string.IsNullOrEmpty(input.ApiKey)))
        {
            // A key is sent to the address it was typed for and to no other.
            keyChanged = row.ProtectedApiKey != null;
            row.ProtectedApiKey = null;
        }

        if (!string.IsNullOrEmpty(input.ApiKey))
        {
            try
            {
                row.ProtectedApiKey = _codec.ProtectSecret(input.ApiKey);
            }
            catch (InvalidOperationException e)
            {
                throw new ReplicationException(e.Message);
            }

            keyChanged = true;
        }

        // A changed schedule counts from now, so enabling a stale replication does not run it at once for the time it was off.
        var restartSchedule = input.Key == 0 || row.ScheduleMinutes != input.ScheduleMinutes || (!row.Enabled && input.Enabled);

        row.Name = name;
        row.RepositoryKey = repository.Key;
        row.Direction = input.Direction;
        row.PeerUrl = peerUrl;
        row.PeerRepository = peerRepository;
        row.Enabled = input.Enabled;
        row.OnDeploy = input.OnDeploy;
        row.ScheduleMinutes = input.ScheduleMinutes;
        row.PropagateDeletes = input.PropagateDeletes;
        row.Conflict = input.Conflict;
        row.Modified = now;
        if (restartSchedule && row.ScheduleMinutes != null) row.ScheduleStart = now;

        AccessAdministration.Audit(db, now, actor, action, name, new
        {
            repository = repository.Name,
            direction = input.Direction.ToString(),
            peer = new Uri(peerUrl).GetLeftPart(UriPartial.Path),
            peerRepository,
            input.Enabled,
            input.OnDeploy,
            input.ScheduleMinutes,
            input.PropagateDeletes,
            conflict = input.Conflict.ToString(),
            apiKeyChanged = keyChanged,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Redact(row);
    }

    private string ValidatePeer(string? url, string? repository, out string peerRepository)
    {
        var settings = new RemoteRepositorySettings { Url = url?.Trim(), Protocol = RemoteProtocol.Npm };
        var problems = settings.Validate(BlockPrivate);
        if (problems.Count > 0) throw new ReplicationException(string.Join(" ", problems).Replace("upstream", "peer", StringComparison.Ordinal));

        peerRepository = repository?.Trim() ?? string.Empty;
        if (!string.Equals(peerRepository, Repository.DefaultName, StringComparison.Ordinal) && !RepositoryNames.IsValid(peerRepository, out var error))
        {
            throw new ReplicationException("The peer repository: " + (error ?? "not a valid name."));
        }

        return settings.UrlOrNull!.AbsoluteUri;
    }

    private static bool SameOrigin(string? a, string? b)
        => Uri.TryCreate(a, UriKind.Absolute, out var x) && Uri.TryCreate(b, UriKind.Absolute, out var y)
           && string.Equals(x.GetLeftPart(UriPartial.Authority), y.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(int key, string? actor, CancellationToken cancellationToken)
    {
        var db = Db;
        _gate.ThrowIfClosed();

        var row = await db.ReplicationConfigs!.FirstOrDefaultAsync(c => c.Key == key, cancellationToken).ConfigureAwait(false);
        if (row == null) return false;
        if (_jobs?.IsRunning(key) == true) throw new ReplicationException("The replication is running; stop it before deleting it.");

        db.ReplicationRuns!.RemoveRange(await db.ReplicationRuns!.Where(r => r.ConfigKey == key).ToListAsync(cancellationToken).ConfigureAwait(false));
        db.ReplicationItems!.RemoveRange(await db.ReplicationItems!.Where(i => i.ConfigKey == key).ToListAsync(cancellationToken).ConfigureAwait(false));
        db.ReplicationConfigs!.Remove(row);
        AccessAdministration.Audit(db, _time.UtcNow, actor, "replication.delete", row.Name, new { });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ReplicationRun>> ListRunsAsync(int configKey, int take, CancellationToken cancellationToken)
    {
        var runs = await Db.ReplicationRuns!.AsNoTracking().Where(r => r.ConfigKey == configKey).ToListAsync(cancellationToken).ConfigureAwait(false);

        return runs.OrderByDescending(r => r.Started).ThenByDescending(r => r.Key).Take(Math.Clamp(take, 1, 500)).ToList();
    }

    /// <inheritdoc/>
    public async Task<ReplicationRun?> GetRunAsync(int runKey, CancellationToken cancellationToken)
        => await Db.ReplicationRuns!.AsNoTracking().FirstOrDefaultAsync(r => r.Key == runKey, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<ReplicationTestResult> TestAsync(ReplicationInput input, CancellationToken cancellationToken)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        var db = Db;

        string peerUrl;
        string peerRepository;
        try
        {
            peerUrl = ValidatePeer(input.PeerUrl, input.PeerRepository, out peerRepository);
        }
        catch (ReplicationException e)
        {
            return new ReplicationTestResult(false, e.Message, 0, null, null);
        }

        var apiKey = string.IsNullOrEmpty(input.ApiKey) ? null : input.ApiKey;
        if (apiKey == null && !input.ClearApiKey && input.Key != 0)
        {
            var stored = await db.ReplicationConfigs!.AsNoTracking().FirstOrDefaultAsync(c => c.Key == input.Key, cancellationToken).ConfigureAwait(false);
            if (stored != null && SameOrigin(stored.PeerUrl, peerUrl)) apiKey = _codec.TryUnprotectSecret(stored.ProtectedApiKey);
        }

        var watch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            if (BlockPrivate)
            {
                var refused = await RemoteAddressPolicy.CheckResolvedAsync(new Uri(peerUrl), timeout.Token).ConfigureAwait(false);
                if (refused != null) return new ReplicationTestResult(false, refused.Replace("upstream", "peer", StringComparison.Ordinal), watch.ElapsedMilliseconds, null, null);
            }

            using var peer = _peers.Create(peerUrl, peerRepository, apiKey, 30);
            var inventory = await peer.GetInventoryAsync(summary: true, timeout.Token).ConfigureAwait(false);

            var message = $"Connected: '{peerRepository}' is a {inventory.Kind.ToString().ToLowerInvariant()} {inventory.Format} repository holding {inventory.Count} item(s).";
            if (!string.IsNullOrWhiteSpace(input.Repository))
            {
                var local = await db.Repositories!.AsNoTracking().FirstOrDefaultAsync(r => r.Name == input.Repository!.Trim(), cancellationToken).ConfigureAwait(false);
                if (local != null && local.Format != inventory.Format)
                {
                    return new ReplicationTestResult(false, $"The peer's repository is a {inventory.Format} repository; '{local.Name}' is {local.Format}.", watch.ElapsedMilliseconds, inventory.Format, inventory.Count);
                }

                if (input.Direction == ReplicationDirection.Push && inventory.Kind != RepositoryKind.Local)
                {
                    return new ReplicationTestResult(false, $"The peer's repository is {inventory.Kind.ToString().ToLowerInvariant()}, so it cannot receive.", watch.ElapsedMilliseconds, inventory.Format, inventory.Count);
                }
            }

            return new ReplicationTestResult(true, message, watch.ElapsedMilliseconds, inventory.Format, inventory.Count);
        }
        catch (ReplicationItemException e)
        {
            return new ReplicationTestResult(false, e.Message, watch.ElapsedMilliseconds, null, null);
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or OperationCanceledException or TimeoutException)
        {
            if (cancellationToken.IsCancellationRequested) throw;

            return new ReplicationTestResult(false, "The peer could not be reached: " + e.GetBaseException().Message, watch.ElapsedMilliseconds, null, null);
        }
    }
}
