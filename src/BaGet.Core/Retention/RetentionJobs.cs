using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BaGet.Core;

public enum RetentionJobState
{
    Running,
    Succeeded,
    Failed,
}

/// <summary>The last run of a policy since the server started (in memory).</summary>
public sealed class RetentionJobStatus
{
    public int PolicyKey { get; init; }

    public bool DryRun { get; init; }

    public RetentionJobState State { get; internal set; }

    /// <summary>The administrator, or <c>scheduler</c>.</summary>
    public string? Actor { get; init; }

    public DateTime Started { get; init; }

    public DateTime? Finished { get; internal set; }

    /// <summary>The report, once the run finished (also when some versions failed).</summary>
    public RetentionReport? Report { get; internal set; }

    /// <summary>Why the run failed as a whole, or null.</summary>
    public string? Error { get; internal set; }
}

/// <summary>
/// Runs retention policies in the background, one run per policy at a time, and keeps the last report of each in memory (a dry
/// run's report is the page's answer). A real run is refused while the <see cref="MaintenanceGate"/> is closed; every run is audited
/// by <see cref="RetentionEngine"/>.
/// </summary>
public sealed class RetentionJobs
{
    private readonly IServiceScopeFactory _scopes;
    private readonly SystemTime _time;
    private readonly MaintenanceGate _gate;
    private readonly ILogger<RetentionJobs> _logger;
    private readonly IHostApplicationLifetime? _lifetime;
    private readonly IJobLease? _lease;
    private readonly ConcurrentDictionary<int, RetentionJobStatus> _status = new();
    private readonly ConcurrentDictionary<int, Task> _running = new();
    private readonly object _lock = new();

    public RetentionJobs(IServiceScopeFactory scopes, SystemTime time, MaintenanceGate gate, ILogger<RetentionJobs> logger, IHostApplicationLifetime? lifetime = null, IJobLease? lease = null)
    {
        _lease = lease;
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _lifetime = lifetime;
    }

    /// <summary>The last run of the policy, or null if none since the server started.</summary>
    public RetentionJobStatus? Get(int policyKey) => _status.TryGetValue(policyKey, out var status) ? status : null;

    /// <summary>Starts a run of the policy in the background.</summary>
    /// <param name="policyKey">The policy.</param>
    /// <param name="dryRun">Whether to only report.</param>
    /// <param name="actor">Who started it.</param>
    /// <param name="refusal">Why it was not started.</param>
    /// <exception cref="MaintenanceException">A real run while the server is read-only.</exception>
    public bool TryStart(int policyKey, bool dryRun, string? actor, out string? refusal)
    {
        if (!dryRun) _gate.ThrowIfClosed();

        // One run per policy in the whole cluster (a dry run changes nothing and takes no lease).
        IAsyncDisposable? held = null;
        if (!dryRun)
        {
            held = _lease.Acquire($"retention:policy:{policyKey}");
            if (held == null)
            {
                refusal = "This policy is already running (on another node).";

                return false;
            }
        }

        var status = new RetentionJobStatus { PolicyKey = policyKey, DryRun = dryRun, State = RetentionJobState.Running, Actor = actor, Started = _time.UtcNow };
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            if (_running.ContainsKey(policyKey))
            {
                held?.Release();
                refusal = "This policy is already running.";

                return false;
            }

            _status[policyKey] = status;
            _running[policyKey] = completion.Task;
        }

        var stopping = _lifetime?.ApplicationStopping ?? CancellationToken.None;
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var report = await scope.ServiceProvider.GetRequiredService<RetentionEngine>().RunAsync(policyKey, dryRun, actor, stopping).ConfigureAwait(false);
                status.Report = report;
                Finish(status, RetentionJobState.Succeeded, null);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Retention policy {Policy} failed", policyKey);
                Finish(status, RetentionJobState.Failed, $"{e.GetType().Name}: {e.Message}");
            }
            finally
            {
                _running.TryRemove(policyKey, out _);
                if (held != null)
                {
                    try { await held.DisposeAsync().ConfigureAwait(false); } catch (Exception) { /* the lease expires by itself */ }
                }

                completion.SetResult(true);
            }
        });

        refusal = null;

        return true;
    }

    /// <summary>Completes when the current run of the policy ends (immediately when none).</summary>
    public Task WaitAsync(int policyKey) => _running.TryGetValue(policyKey, out var task) ? task : Task.CompletedTask;

    private void Finish(RetentionJobStatus status, RetentionJobState state, string? error)
    {
        status.Error = error;
        status.Finished = _time.UtcNow;
        status.State = state;
    }
}

/// <summary>
/// Starts the policies whose schedule is due (<see cref="RetentionPolicy.ScheduleHours"/> after the later of the last run and the
/// time the schedule was set), and writes the download times <see cref="ArtifactUsageRecorder"/> collected. Idle while the server is
/// read-only for maintenance (the due policies run on the next pass after it reopens) and with a database that cannot hold policies.
/// </summary>
public sealed class RetentionWorker : BackgroundService
{
    /// <summary>How often the worker looks.</summary>
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly RetentionJobs _jobs;
    private readonly ArtifactUsageRecorder _usage;
    private readonly MaintenanceGate _gate;
    private readonly SystemTime _time;
    private readonly ILogger<RetentionWorker> _logger;
    private readonly IJobLease? _lease;

    public RetentionWorker(IServiceScopeFactory scopes, RetentionJobs jobs, ArtifactUsageRecorder usage, MaintenanceGate gate, SystemTime time, ILogger<RetentionWorker> logger, IJobLease? lease = null)
    {
        _lease = lease;
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _usage = usage ?? throw new ArgumentNullException(nameof(usage));
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
                await RunDueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Retention pass failed; it is retried on the next one");
            }
        }
    }

    /// <summary>One pass: flush the download times, then run every due policy (one after the other) and wait for them.</summary>
    /// <returns>The keys of the policies started.</returns>
    public async Task<IReadOnlyList<int>> RunDueAsync(CancellationToken cancellationToken)
    {
        await _usage.FlushAsync(_scopes, cancellationToken).ConfigureAwait(false);
        if (_gate.IsClosed) return Array.Empty<int>();

        // One scheduling pass in the whole cluster: the node holding this lease starts the due policies and waits for them.
        await using var pass = await _lease.AcquireAsync("retention:schedule", cancellationToken).ConfigureAwait(false);
        if (pass == null) return Array.Empty<int>();

        List<RetentionPolicy> due;
        using (var scope = _scopes.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IContext>();
            if (context is NullContext) return Array.Empty<int>();

            var now = _time.UtcNow;
            var candidates = await context.RetentionPolicies!.AsNoTracking().Where(p => p.Enabled && p.ScheduleHours != null).ToListAsync(cancellationToken).ConfigureAwait(false);
            due = candidates.Where(p => IsDue(p, now)).OrderBy(p => p.Key).ToList();
        }

        var started = new List<int>();
        foreach (var policy in due)
        {
            try
            {
                if (!_jobs.TryStart(policy.Key, dryRun: false, "scheduler", out _)) continue;
            }
            catch (MaintenanceException)
            {
                break;
            }

            started.Add(policy.Key);
            await _jobs.WaitAsync(policy.Key).ConfigureAwait(false);
        }

        return started;
    }

    /// <summary>Whether the schedule of <paramref name="policy"/> is due at <paramref name="now"/>.</summary>
    public static bool IsDue(RetentionPolicy policy, DateTime now)
    {
        if (!policy.Enabled || policy.ScheduleHours is not int hours) return false;

        var basis = new[] { policy.LastRun, policy.ScheduleStart, policy.Created }.Where(d => d != null).Select(d => d!.Value).DefaultIfEmpty(DateTime.MinValue).Max();

        return basis.AddHours(hours) <= now;
    }
}
