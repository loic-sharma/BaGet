using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BaGet.Validation.Pipeline;

namespace BaGet.Validation.Queue;

/// <summary>
/// Runs queued submissions one at a time. On startup it re-queues every submission still
/// pending, so a restart delays validation but loses nothing.
/// </summary>
public class ValidationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IValidationQueue _queue;
    private readonly ILogger<ValidationWorker> _logger;
    private readonly IJobLease? _lease;
    private readonly ClusterRuntime? _cluster;

    public ValidationWorker(IServiceScopeFactory scopes, IValidationQueue queue, ILogger<ValidationWorker> logger, IJobLease? lease = null, ClusterRuntime? cluster = null)
    {
        _lease = lease;
        _cluster = cluster;
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Yield so that host startup does not wait on the database.
        await Task.Yield();

        await RequeuePendingAsync(stoppingToken);

        // A node that died mid-validation leaves its submission pending with nobody to resume it until a restart; in a cluster the
        // survivors offer every pending submission again now and then (a submission already done is skipped by the runner).
        if (_cluster?.IsEnabled == true) _ = RequeueRegularlyAsync(stoppingToken);

        await foreach (var submissionId in _queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                // Every node queues the pending submissions when it starts and the one that took a push queues it: only one node validates
                // a submission at a time. A node that finds it taken skips it (the holder finishes it, or the periodic requeue offers it again).
                await using var held = await _lease.AcquireAsync("validation:" + submissionId, stoppingToken);
                if (held == null) continue;

                using var scope = _scopes.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<PackageValidationRunner>();

                await runner.RunAsync(submissionId, stoppingToken);
                _failures.TryRemove(submissionId, out _);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (MaintenanceException e)
            {
                // The database is read-only for now. The submission is still pending, so try it again
                // once the gate may have reopened rather than leaving it until the next start.
                _logger.LogWarning("Validation of submission {SubmissionId} postponed: {Reason}", submissionId, e.Reason);
                await RetryLaterAsync(submissionId, stoppingToken);
            }
            catch (Exception e)
            {
                // Still pending: offered again after a growing delay, a bounded number of times; then it waits for the
                // next start, or for an administrator to reject it.
                var attempt = _failures.AddOrUpdate(submissionId, 1, (_, n) => n + 1);
                if (attempt <= MaxFailureRetries)
                {
                    var delay = TimeSpan.FromTicks(FailureRetryDelay.Ticks << (attempt - 1));
                    _logger.LogError(e, "Validation of submission {SubmissionId} failed (attempt {Attempt}); retrying in {Delay}", submissionId, attempt, delay);
                    _ = RetryLaterAsync(submissionId, delay, stoppingToken);
                }
                else
                {
                    _failures.TryRemove(submissionId, out _);
                    _logger.LogError(e, "Validation of submission {SubmissionId} failed {Attempts} times; it stays pending until the next start or an administrator rejects it", submissionId, attempt);
                }
            }
        }
    }

    /// <summary>How long a submission waits after a maintenance refusal before it is queued again.</summary>
    internal static TimeSpan MaintenanceRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The first delay before a failed submission is queued again; it doubles with each failure.</summary>
    internal static TimeSpan FailureRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How many times a failed submission is queued again before it is left pending.</summary>
    internal const int MaxFailureRetries = 5;

    private readonly ConcurrentDictionary<string, int> _failures = new();

    private Task RetryLaterAsync(string submissionId, CancellationToken cancellationToken)
        => RetryLaterAsync(submissionId, MaintenanceRetryDelay, cancellationToken);

    private async Task RetryLaterAsync(string submissionId, TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            await _queue.EnqueueAsync(submissionId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down: the submission stays pending and is re-queued at the next start.
        }
    }

    /// <summary>How often a cluster node offers the pending submissions again.</summary>
    internal static TimeSpan ClusterRequeueInterval { get; set; } = TimeSpan.FromMinutes(1);

    private async Task RequeueRegularlyAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(ClusterRequeueInterval, cancellationToken);
                await RequeuePendingAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RequeuePendingAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            if (!scope.ServiceProvider.GetRequiredService<IOptions<ValidationOptions>>().Value.Enabled) return;
            if (scope.ServiceProvider.GetRequiredService<IContext>() is NullContext) return;

            var runner = scope.ServiceProvider.GetRequiredService<PackageValidationRunner>();
            #pragma warning disable CS8602
            foreach (var submissionId in await runner.GetPendingSubmissionIdsAsync(cancellationToken))
            #pragma warning restore CS8602
            {
                await _queue.EnqueueAsync(submissionId, cancellationToken);
            }
        }
        catch (Exception e) when (!(e is OperationCanceledException))
        {
            // The database may not be migrated yet on a first start.
            _logger.LogWarning(e, "Could not re-queue pending validations");
        }
    }
}