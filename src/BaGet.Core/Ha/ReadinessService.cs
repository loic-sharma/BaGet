using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BaGet.Core;

/// <summary>One probe of the readiness check.</summary>
/// <param name="Name"><c>database</c>, <c>storage</c> or <c>cluster</c>.</param>
/// <param name="Healthy">Whether it answered.</param>
/// <param name="Detail">A short, credential-free note (the exception type when it failed).</param>
/// <param name="Informational">The probe is reported but a failure does not make the node unready (the shared store: a node keeps serving without it).</param>
public sealed record HealthProbe(string Name, bool Healthy, string? Detail, bool Informational = false);

/// <summary>The result of the readiness check.</summary>
/// <param name="CheckedAt">When it ran (UTC).</param>
/// <param name="Probes">Each probe.</param>
public sealed record ReadinessReport(DateTime CheckedAt, IReadOnlyList<HealthProbe> Probes)
{
    /// <summary>Every probe that decides readiness answered (an informational probe, the shared store, never takes the node out of rotation).</summary>
    public bool IsReady => Probes.All(p => p.Healthy || p.Informational);

    /// <summary>The failing probes (informational ones too) as one line, or null.</summary>
    public string? Problem
    {
        get
        {
            var failing = Probes.Where(p => !p.Healthy).Select(p => p.Name + (p.Detail == null ? "" : " (" + p.Detail + ")")).ToList();

            return failing.Count == 0 ? null : string.Join(", ", failing);
        }
    }
}

/// <summary>
/// What <c>/health/ready</c> and the cluster page report: whether this node can serve. It reaches the database (a connection), the
/// storage (writes and deletes a one-byte probe object) and, in a cluster, the shared store, which is only <i>reported</i>
/// (<see cref="HealthProbe.Informational"/>): a node keeps serving without Redis, so a Redis outage must not take every node out of rotation at once. The answer is kept for
/// <see cref="CacheFor"/>, so a load balancer polling every second costs one check per interval. Readiness never depends on the
/// read-only gate (a node refusing writes still serves reads).
/// </summary>
public sealed class ReadinessService
{
    /// <summary>How long an answer is reused.</summary>
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopes;
    private readonly ClusterRuntime _cluster;
    private readonly ILogger<ReadinessService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ReadinessReport? _last;

    /// <summary>Creates the service.</summary>
    public ReadinessService(IServiceScopeFactory scopes, ClusterRuntime cluster, ILogger<ReadinessService> logger)
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _cluster = cluster ?? throw new ArgumentNullException(nameof(cluster));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The last answer, without running a check (null before the first).</summary>
    public ReadinessReport? Last => _last;

    /// <summary>Runs the probes unless a recent answer exists.</summary>
    /// <param name="force">Run them even if the last answer is recent.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    public async Task<ReadinessReport> CheckAsync(bool force, CancellationToken cancellationToken)
    {
        var last = _last;
        if (!force && last != null && DateTime.UtcNow - last.CheckedAt < CacheFor) return last;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            last = _last;
            if (!force && last != null && DateTime.UtcNow - last.CheckedAt < CacheFor) return last;

            var probes = new List<HealthProbe>
            {
                await ProbeAsync("database", DatabaseAsync, cancellationToken).ConfigureAwait(false),
                await ProbeAsync("storage", StorageAsync, cancellationToken).ConfigureAwait(false),
            };
            if (_cluster.IsEnabled) probes.Add(await ProbeAsync("cluster", ClusterAsync, cancellationToken, informational: true).ConfigureAwait(false));

            return _last = new ReadinessReport(DateTime.UtcNow, probes);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<HealthProbe> ProbeAsync(string name, Func<CancellationToken, Task<string?>> probe, CancellationToken cancellationToken, bool informational = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            return new HealthProbe(name, true, await probe(timeout.Token).ConfigureAwait(false), informational);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The type only: a message can carry a connection string.
            _logger.LogWarning("Readiness probe {Probe} failed: {Error}", name, e.GetType().Name);

            return new HealthProbe(name, false, e is OperationCanceledException ? "timeout" : e.GetType().Name, informational);
        }
    }

    private async Task<string?> DatabaseAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        if (context is NullContext) return "not checked (Azure Table)";
        if (!await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false)) throw new InvalidOperationException("cannot connect");

        return null;
    }

    private async Task<string?> StorageAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();
        var path = "health/" + _cluster.NodeId.Replace('|', '_') + ".probe";
        using (var content = new MemoryStream(Encoding.ASCII.GetBytes("ok")))
        {
            await storage.PutAsync(path, content, "text/plain", cancellationToken).ConfigureAwait(false);
        }

        await storage.DeleteAsync(path, cancellationToken).ConfigureAwait(false);

        return null;
    }

    private async Task<string?> ClusterAsync(CancellationToken cancellationToken)
    {
        if (!await _cluster.Store!.PingAsync(cancellationToken).ConfigureAwait(false)) throw new InvalidOperationException("no answer");

        return _cluster.Store.Description;
    }
}
