using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BaGet.Core;

/// <summary>A node as it publishes itself.</summary>
/// <param name="Id">The node's name.</param>
/// <param name="Started">When it started (UTC).</param>
/// <param name="Seen">Its latest heartbeat (UTC).</param>
/// <param name="Version">The BaGet version it runs.</param>
/// <param name="Ready">Whether its last readiness check passed.</param>
/// <param name="Problem">What failed, when it did not.</param>
/// <param name="GateClosed">Whether its read-only gate is closed.</param>
public sealed record NodeInfo(string Id, DateTime Started, DateTime Seen, string Version, bool Ready, string? Problem, bool GateClosed);

/// <summary>
/// The node's side of a cluster: publishes a heartbeat and brings the state the nodes share into this process every
/// <see cref="HaOptions.SyncInterval"/>:
/// <list type="bullet">
/// <item>the read-only <see cref="MaintenanceGate"/> (a closure published by one node closes all; it lives while its node renews it);</item>
/// <item>the access model's version (a change committed on one node makes the others reload their snapshot);</item>
/// <item>the database copy's progress (a copy runs on the node that accepted it; every node shows it).</item>
/// </list>
/// Idle without <c>Ha:Enabled</c>. A store that cannot be reached is logged and retried; the node keeps serving with what it has.
/// </summary>
public sealed class ClusterSyncService : BackgroundService
{
    internal static readonly JsonSerializerOptions Json = CreateJson();

    private readonly ClusterRuntime _cluster;
    private readonly MaintenanceGate _gate;
    private readonly AccessModelVersion _access;
    private readonly ReadinessService _readiness;
    private readonly ILogger<ClusterSyncService> _logger;
    private readonly DatabaseCopyService? _copy;
    private long _accessSeen = -1;

    /// <summary>Creates the service.</summary>
    public ClusterSyncService(
        ClusterRuntime cluster,
        MaintenanceGate gate,
        AccessModelVersion access,
        ReadinessService readiness,
        ILogger<ClusterSyncService> logger,
        DatabaseCopyService? copy = null)
    {
        _cluster = cluster ?? throw new ArgumentNullException(nameof(cluster));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _copy = copy;
    }

    /// <summary>Starts publishing this node's own gate and access changes at once (not only at the next tick).</summary>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (_cluster.IsEnabled)
        {
            var store = _cluster.Store!;
            _gate.Publisher = reason =>
            {
                try
                {
                    if (reason != null) store.Set(ClusterRuntime.Keys.Gate, GateValue(reason), _cluster.LeaseDuration);
                    else store.Delete(ClusterRuntime.Keys.Gate);
                }
                catch (Exception e)
                {
                    // The next tick publishes it again.
                    _logger.LogWarning(e, "Could not publish the read-only gate to the cluster; retried at the next sync.");
                }
            };
            _access.Publisher = () =>
            {
                try
                {
                    var n = store.Increment(ClusterRuntime.Keys.AccessVersion, TimeSpan.FromDays(30));
                    Interlocked.CompareExchange(ref _accessSeen, n, n - 1);
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "Could not tell the cluster the access model changed; the other nodes reload within 30 seconds.");
                }
            };
        }

        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        if (!_cluster.IsEnabled) return;

        _gate.Publisher = null;
        _access.Publisher = null;
        try
        {
            var store = _cluster.Store!;
            await store.DeleteAsync(ClusterRuntime.Keys.Node(_cluster.NodeId), CancellationToken.None).ConfigureAwait(false);
            if (_gate.LocalReason != null) await store.DeleteAsync(ClusterRuntime.Keys.Gate, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Could not remove this node from the cluster listing; it expires by itself.");
        }
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_cluster.IsEnabled) return;

        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Cluster sync failed; retried in {Interval}.", _cluster.SyncInterval);
            }

            try
            {
                await Task.Delay(_cluster.SyncInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One round: heartbeat, then gate, access model and copy status. Public so a test can force it.</summary>
    public async Task SyncAsync(CancellationToken cancellationToken)
    {
        var store = _cluster.Store ?? throw new InvalidOperationException("Not in a cluster.");

        // Each part is independent: a failure in one must not stop the others.
        var errors = new List<Exception>();
        await TryAsync(() => HeartbeatAsync(store, cancellationToken), errors).ConfigureAwait(false);
        await TryAsync(() => SyncGateAsync(store, cancellationToken), errors).ConfigureAwait(false);
        await TryAsync(() => SyncAccessAsync(store, cancellationToken), errors).ConfigureAwait(false);
        await TryAsync(() => SyncCopyAsync(store, cancellationToken), errors).ConfigureAwait(false);

        if (errors.Count > 0) throw errors.Count == 1 ? errors[0] : new AggregateException(errors);
    }

    private static async Task TryAsync(Func<Task> step, List<Exception> errors)
    {
        try
        {
            await step().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            errors.Add(e);
        }
    }

    private async Task HeartbeatAsync(IClusterStore store, CancellationToken cancellationToken)
    {
        var readiness = _readiness.Last;
        if (readiness == null || DateTime.UtcNow - readiness.CheckedAt > ReadinessService.CacheFor)
        {
            readiness = await _readiness.CheckAsync(force: false, cancellationToken).ConfigureAwait(false);
        }

        var info = new NodeInfo(
            _cluster.NodeId,
            _cluster.Started,
            DateTime.UtcNow,
            typeof(ClusterSyncService).Assembly.GetName().Version?.ToString() ?? "?",
            readiness.IsReady,
            readiness.Problem,
            _gate.IsClosed);
        await store.SetAsync(ClusterRuntime.Keys.Node(_cluster.NodeId), JsonSerializer.Serialize(info, Json), _cluster.HeartbeatTtl, cancellationToken).ConfigureAwait(false);
    }

    private async Task SyncGateAsync(IClusterStore store, CancellationToken cancellationToken)
    {
        var mine = _gate.LocalReason;
        if (mine != null)
        {
            // Keep our closure alive; if its key vanished (expired during a pause) this recreates it.
            await store.SetAsync(ClusterRuntime.Keys.Gate, GateValue(mine), _cluster.LeaseDuration, cancellationToken).ConfigureAwait(false);

            return;
        }

        var value = await store.GetAsync(ClusterRuntime.Keys.Gate, cancellationToken).ConfigureAwait(false);
        if (value == null)
        {
            _gate.ApplyRemote(null);

            return;
        }

        var separator = value.IndexOf('|');
        var owner = separator < 0 ? string.Empty : value.Substring(0, separator);
        var reason = separator < 0 ? value : value.Substring(separator + 1);
        if (owner == _cluster.NodeId)
        {
            // A closure this node reopened but could not remove.
            await store.DeleteAsync(ClusterRuntime.Keys.Gate, cancellationToken).ConfigureAwait(false);
            _gate.ApplyRemote(null);

            return;
        }

        _gate.ApplyRemote(reason);
    }

    private async Task SyncAccessAsync(IClusterStore store, CancellationToken cancellationToken)
    {
        // No counter yet (no node has changed the access model) reads as 0.
        var value = await store.GetAsync(ClusterRuntime.Keys.AccessVersion, cancellationToken).ConfigureAwait(false);
        var seen = 0L;
        if (value != null && !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out seen)) return;

        var previous = Interlocked.Exchange(ref _accessSeen, seen);
        // The first read only learns where the counter is.
        if (previous != -1 && previous != seen) _access.BumpFromCluster();
    }

    private async Task SyncCopyAsync(IClusterStore store, CancellationToken cancellationToken)
    {
        if (_copy == null) return;

        var local = _copy.LocalStatus;
        if (local != DatabaseCopyStatus.Idle)
        {
            await store.SetAsync(ClusterRuntime.Keys.CopyStatus, JsonSerializer.Serialize(local, Json), TimeSpan.FromHours(1), cancellationToken).ConfigureAwait(false);
            _copy.RemoteStatus = null;

            return;
        }

        var value = await store.GetAsync(ClusterRuntime.Keys.CopyStatus, cancellationToken).ConfigureAwait(false);
        _copy.RemoteStatus = value == null ? null : JsonSerializer.Deserialize<DatabaseCopyStatus>(value, Json);
    }

    private string GateValue(string reason) => _cluster.NodeId + "|" + reason;

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}

/// <summary>What the cluster page shows.</summary>
/// <param name="Enabled">Whether this node is in a cluster.</param>
/// <param name="ThisNode">This node's name.</param>
/// <param name="Backend">The store, without credentials.</param>
/// <param name="BackendReachable">Whether the store answered.</param>
/// <param name="Nodes">The nodes with a recent heartbeat, this one included.</param>
/// <param name="Leases">The job leases held now.</param>
/// <param name="GateReason">Why the cluster is read-only, or null.</param>
/// <param name="Readiness">This node's readiness check.</param>
public sealed record ClusterStatus(
    bool Enabled,
    string ThisNode,
    string? Backend,
    bool BackendReachable,
    IReadOnlyList<NodeInfo> Nodes,
    IReadOnlyList<LeaseInfo> Leases,
    string? GateReason,
    ReadinessReport Readiness);

/// <summary>Reads the cluster's shared state for the administrator's page.</summary>
public interface IClusterStatusService
{
    /// <summary>The current status (cheap on a single node, which lists only itself).</summary>
    Task<ClusterStatus> GetAsync(CancellationToken cancellationToken);
}

/// <summary>The default <see cref="IClusterStatusService"/>.</summary>
public sealed class ClusterStatusService : IClusterStatusService
{
    private readonly ClusterRuntime _cluster;
    private readonly ReadinessService _readiness;
    private readonly MaintenanceGate _gate;

    /// <summary>Creates the service.</summary>
    public ClusterStatusService(ClusterRuntime cluster, ReadinessService readiness, MaintenanceGate gate)
    {
        _cluster = cluster ?? throw new ArgumentNullException(nameof(cluster));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    /// <inheritdoc/>
    public async Task<ClusterStatus> GetAsync(CancellationToken cancellationToken)
    {
        var readiness = await _readiness.CheckAsync(force: false, cancellationToken).ConfigureAwait(false);
        var store = _cluster.Store;
        if (store == null)
        {
            var self = new NodeInfo(_cluster.NodeId, _cluster.Started, DateTime.UtcNow, typeof(ClusterStatusService).Assembly.GetName().Version?.ToString() ?? "?", readiness.IsReady, readiness.Problem, _gate.IsClosed);

            return new ClusterStatus(false, _cluster.NodeId, null, false, new[] { self }, Array.Empty<LeaseInfo>(), _gate.Reason, readiness);
        }

        var reachable = false;
        var nodes = new List<NodeInfo>();
        var leases = new List<LeaseInfo>();
        string? gate = _gate.Reason;
        try
        {
            reachable = await store.PingAsync(cancellationToken).ConfigureAwait(false);
            foreach (var (_, value) in await store.ListAsync(ClusterRuntime.Keys.NodePrefix, cancellationToken).ConfigureAwait(false))
            {
                var node = JsonSerializer.Deserialize<NodeInfo>(value, ClusterSyncService.Json);
                if (node != null) nodes.Add(node);
            }

            foreach (var (key, value) in await store.ListAsync(ClusterRuntime.Keys.LeasePrefix, cancellationToken).ConfigureAwait(false))
            {
                var lease = LeaseInfo.Parse(key.Substring(ClusterRuntime.Keys.LeasePrefix.Length), value);
                if (lease != null) leases.Add(lease);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            reachable = false;
        }

        return new ClusterStatus(
            true,
            _cluster.NodeId,
            store.Description,
            reachable,
            nodes.OrderBy(n => n.Id, StringComparer.Ordinal).ToList(),
            leases.OrderBy(l => l.Job, StringComparer.Ordinal).ToList(),
            gate,
            readiness);
    }
}
