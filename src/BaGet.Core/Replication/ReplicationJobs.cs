using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BaGet.Core;

/// <summary>What the page knows of a replication started since the server began (in memory); the log table is the durable record.</summary>
public sealed class ReplicationJobStatus
{
    public int ConfigKey { get; init; }

    public DateTime Started { get; init; }

    public DateTime? Finished { get; internal set; }

    /// <summary>True while the run is going.</summary>
    public bool Running => Finished == null;

    /// <summary>Why the job did not even begin a run (read-only server, disabled configuration, lease held elsewhere), or null.</summary>
    public string? Refusal { get; internal set; }
}

/// <summary>
/// Remembers which repositories received something (a package, a version, a file, a tag) so deploy-triggered pushes can start after a
/// short quiet period instead of once per file. Filled by <see cref="ReplicationDeployInterceptor"/>.
/// </summary>
public sealed class ReplicationTrigger
{
    /// <summary>How long a repository must be quiet before its deploy-triggered replications start.</summary>
    internal static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<int, DateTime> _pending = new();
    private readonly SystemTime _time;

    public ReplicationTrigger(SystemTime time)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>Records that the repository received something now.</summary>
    public void Notify(int repositoryKey) => _pending[repositoryKey] = _time.UtcNow;

    /// <summary>Removes and returns the repositories that have been quiet for <paramref name="quiet"/>.</summary>
    public IReadOnlyList<int> TakeQuiet(TimeSpan quiet)
    {
        var now = _time.UtcNow;
        var due = _pending.Where(p => now - p.Value >= quiet).Select(p => p.Key).ToList();
        foreach (var key in due) _pending.TryRemove(key, out _);

        return due;
    }
}

/// <summary>A database context that knows the repository it works in (every <see cref="AbstractContext{TContext}"/>).</summary>
public interface IRepositoryScopedContext
{
    /// <summary>The repository this context works in now.</summary>
    int ScopeRepositoryKey { get; }
}

/// <summary>
/// Tells <see cref="ReplicationTrigger"/> after a save that committed a new package, npm version, Maven file or Docker tag (or moved
/// one), in the repository the context worked in. It never reads or changes the rows.
/// </summary>
public sealed class ReplicationDeployInterceptor : SaveChangesInterceptor
{
    private readonly ReplicationTrigger _trigger;
    private readonly ConditionalWeakTable<DbContext, StrongBox<int>> _pending = new();

    public ReplicationDeployInterceptor(ReplicationTrigger trigger)
    {
        _trigger = trigger ?? throw new ArgumentNullException(nameof(trigger));
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Mark(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Mark(eventData.Context);
        return new ValueTask<InterceptionResult<int>>(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Commit(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Commit(eventData.Context);
        return new ValueTask<int>(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Forget(eventData.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData.Context);
        return Task.CompletedTask;
    }

    private void Mark(DbContext? context)
    {
        if (context is not IRepositoryScopedContext db) return;

        // A package row is also modified by every download count; only a new row is a deploy. A Docker tag is also a deploy when it moves.
        var deployed = context.ChangeTracker.Entries().Any(e => e.Entity switch
        {
            Package or NpmVersion or MavenFile => e.State == EntityState.Added,
            OciTag => e.State is EntityState.Added or EntityState.Modified,
            _ => false,
        });
        if (deployed) _pending.AddOrUpdate(context, new StrongBox<int>(db.ScopeRepositoryKey));
    }

    private void Commit(DbContext? context)
    {
        if (context != null && _pending.TryGetValue(context, out var key))
        {
            _pending.Remove(context);
            _trigger.Notify(key.Value);
        }
    }

    private void Forget(DbContext? context)
    {
        if (context != null) _pending.Remove(context);
    }
}

/// <summary>
/// Runs replications in the background, one run per configuration at a time (and, through <see cref="IJobLease"/>, one in the cluster).
/// Stopping the server cancels the runs; each ends as <see cref="ReplicationRunStatus.Interrupted"/> and a later run continues.
/// </summary>
public sealed class ReplicationJobs
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IJobLease _lease;
    private readonly SystemTime _time;
    private readonly MaintenanceGate _gate;
    private readonly ILogger<ReplicationJobs> _logger;
    private readonly IHostApplicationLifetime? _lifetime;
    private readonly ConcurrentDictionary<int, ReplicationJobStatus> _status = new();
    private readonly ConcurrentDictionary<int, (Task Task, CancellationTokenSource Cancel)> _running = new();
    private readonly object _lock = new();

    public ReplicationJobs(IServiceScopeFactory scopes, IJobLease lease, SystemTime time, MaintenanceGate gate, ILogger<ReplicationJobs> logger, IHostApplicationLifetime? lifetime = null)
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _lease = lease ?? throw new ArgumentNullException(nameof(lease));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _lifetime = lifetime;
    }

    /// <summary>The name of the lease that guards a configuration's runs.</summary>
    public static string LeaseName(int configKey) => "replication:" + configKey;

    /// <summary>The last job of the configuration since the server started, or null.</summary>
    public ReplicationJobStatus? Get(int configKey) => _status.TryGetValue(configKey, out var status) ? status : null;

    /// <summary>Whether this process is running the configuration now.</summary>
    public bool IsRunning(int configKey) => _running.ContainsKey(configKey);

    /// <summary>Asks the running replication to stop (it ends as interrupted); false when none is running here.</summary>
    public bool Cancel(int configKey)
    {
        if (!_running.TryGetValue(configKey, out var running)) return false;

        try
        {
            running.Cancel.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        return true;
    }

    /// <summary>Starts a run in the background.</summary>
    /// <param name="configKey">The configuration.</param>
    /// <param name="trigger"><c>manual</c>, <c>resume</c>, <c>schedule</c> or <c>deploy</c>.</param>
    /// <param name="actor">Who started it.</param>
    /// <param name="resumedFrom">The interrupted or failed run being continued, or null.</param>
    /// <param name="refusal">Why it was not started.</param>
    /// <param name="notRun">Called when the run was accepted here but never ran because another node holds the configuration's lease
    /// (the caller puts back what it had consumed to start the run, e.g. a deploy notice).</param>
    /// <exception cref="MaintenanceException">The server is read-only.</exception>
    public bool TryStart(int configKey, string trigger, string? actor, int? resumedFrom, out string? refusal, Action? notRun = null)
    {
        _gate.ThrowIfClosed();

        var cancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime?.ApplicationStopping ?? CancellationToken.None);
        var status = new ReplicationJobStatus { ConfigKey = configKey, Started = _time.UtcNow };
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            if (_running.ContainsKey(configKey))
            {
                cancel.Dispose();
                refusal = "This replication is already running.";

                return false;
            }

            _status[configKey] = status;
            _running[configKey] = (completion.Task, cancel);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await using var lease = await _lease.TryAcquireAsync(LeaseName(configKey), cancel.Token).ConfigureAwait(false);
                if (lease == null)
                {
                    status.Refusal = "Another node is running this replication.";
                    notRun?.Invoke();

                    return;
                }

                using var scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ReplicationEngine>().RunAsync(configKey, trigger, actor, resumedFrom, cancel.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is ReplicationException or MaintenanceException)
            {
                status.Refusal = e.Message;
            }
            catch (OperationCanceledException)
            {
                status.Refusal = "Stopped.";
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Replication {Replication} failed", configKey);
                status.Refusal = $"{e.GetType().Name}: {e.Message}";
            }
            finally
            {
                status.Finished = _time.UtcNow;
                _running.TryRemove(configKey, out _);
                cancel.Dispose();
                completion.SetResult(true);
            }
        });

        refusal = null;

        return true;
    }

    /// <summary>Completes when the current run of the configuration ends (immediately when none).</summary>
    public Task WaitAsync(int configKey) => _running.TryGetValue(configKey, out var running) ? running.Task : Task.CompletedTask;
}

/// <summary>
/// Starts the replications that are due: scheduled ones (<see cref="ReplicationConfig.ScheduleMinutes"/> after the later of the last run
/// and the time the schedule was set), deploy-triggered pushes once their repository has been quiet, and marks runs that a stopped process
/// left as <see cref="ReplicationRunStatus.Running"/> as interrupted (only when the configuration's lease is free, so a run on another
/// node is left alone). Idle while the server is read-only and with a database that cannot hold configurations.
/// </summary>
public sealed class ReplicationWorker : BackgroundService
{
    /// <summary>How often the worker looks.</summary>
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopes;
    private readonly ReplicationJobs _jobs;
    private readonly ReplicationTrigger _trigger;
    private readonly IJobLease _lease;
    private readonly MaintenanceGate _gate;
    private readonly SystemTime _time;
    private readonly ILogger<ReplicationWorker> _logger;

    public ReplicationWorker(IServiceScopeFactory scopes, ReplicationJobs jobs, ReplicationTrigger trigger, IJobLease lease, MaintenanceGate gate, SystemTime time, ILogger<ReplicationWorker> logger)
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _trigger = trigger ?? throw new ArgumentNullException(nameof(trigger));
        _lease = lease ?? throw new ArgumentNullException(nameof(lease));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // The wait comes first: the database may still be migrating when the host starts.
            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await RunDueAsync(ReplicationTrigger.QuietPeriod, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Replication pass failed; it is retried on the next one");
            }
        }
    }

    /// <summary>One pass.</summary>
    /// <param name="quiet">How long a repository must have been quiet for its deploy-triggered replications to start.</param>
    /// <param name="cancellationToken">Stops the pass.</param>
    /// <returns>The keys of the configurations started.</returns>
    public async Task<IReadOnlyList<int>> RunDueAsync(TimeSpan quiet, CancellationToken cancellationToken)
    {
        if (_gate.IsClosed) return Array.Empty<int>();

        var deployed = _trigger.TakeQuiet(quiet);
        List<ReplicationConfig> due;
        using (var scope = _scopes.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IContext>();
            if (context is NullContext) return Array.Empty<int>();

            await SweepOrphansAsync(context, cancellationToken).ConfigureAwait(false);

            var now = _time.UtcNow;
            var enabled = await context.ReplicationConfigs!.AsNoTracking().Where(c => c.Enabled).ToListAsync(cancellationToken).ConfigureAwait(false);
            due = enabled
                .Where(c => IsDue(c, now) || (c.OnDeploy && c.Direction == ReplicationDirection.Push && deployed.Contains(c.RepositoryKey)))
                .OrderBy(c => c.Key)
                .ToList();
        }

        var started = new List<int>();
        foreach (var config in due)
        {
            var trigger = deployed.Contains(config.RepositoryKey) && config.OnDeploy && config.Direction == ReplicationDirection.Push ? "deploy" : "schedule";
            try
            {
                // A deploy notice is consumed by TakeQuiet; it must survive a run that does not happen, here (already running) or on
                // another node that holds the lease (that run may have listed its inventory before the deploy).
                var repositoryKey = config.RepositoryKey;
                Action? notRun = trigger == "deploy" ? () => _trigger.Notify(repositoryKey) : null;
                if (_jobs.TryStart(config.Key, trigger, "scheduler", null, out _, notRun))
                {
                    started.Add(config.Key);
                }
                else if (trigger == "deploy")
                {
                    // Still running: the news must not be lost, the next pass starts it again.
                    _trigger.Notify(config.RepositoryKey);
                }
            }
            catch (MaintenanceException)
            {
                break;
            }
        }

        return started;
    }

    // A run left "Running" by a process that stopped. It is ours to mark only when nothing here runs it and its lease is free.
    private async Task SweepOrphansAsync(IContext context, CancellationToken cancellationToken)
    {
        var running = await context.ReplicationRuns!.Where(r => r.Status == ReplicationRunStatus.Running).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (running.Count == 0) return;

        foreach (var run in running)
        {
            if (_jobs.IsRunning(run.ConfigKey)) continue;

            await using var lease = await _lease.TryAcquireAsync(ReplicationJobs.LeaseName(run.ConfigKey), cancellationToken).ConfigureAwait(false);
            if (lease == null) continue;

            // The row was read before the lease: the run may have finished meanwhile. Judge it again, and save while the lease is held.
            var key = run.Key;
            var stillRunning = await context.ReplicationRuns!.AsNoTracking().AnyAsync(r => r.Key == key && r.Status == ReplicationRunStatus.Running, cancellationToken).ConfigureAwait(false);
            if (!stillRunning) continue;

            run.Status = ReplicationRunStatus.Interrupted;
            run.Finished = _time.UtcNow;
            run.Error = "The server stopped during this run; start the replication again to continue.";
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Whether the schedule of <paramref name="config"/> is due at <paramref name="now"/>.</summary>
    public static bool IsDue(ReplicationConfig config, DateTime now)
    {
        if (!config.Enabled || config.ScheduleMinutes is not int minutes) return false;

        var basis = new[] { config.LastRun, config.ScheduleStart, config.Created }.Where(d => d != null).Select(d => d!.Value).DefaultIfEmpty(DateTime.MinValue).Max();

        return basis.AddMinutes(minutes) <= now;
    }
}
