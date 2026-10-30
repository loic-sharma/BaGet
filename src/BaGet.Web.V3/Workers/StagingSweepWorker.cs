
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BaGet.Web.V3.Workers;

/// <summary>
/// Removes uploads that were staged and then neither confirmed nor cancelled - a closed
/// browser tab, a lost session. Confirming and cancelling remove their own upload, so
/// this only collects what nobody came back for; without it staging would grow without
/// bound.
/// </summary>
public class StagingSweepWorker : BackgroundService
{
    /// <summary>
    /// How long an unconfirmed upload is kept. Long enough to read a preview, decide, and
    /// come back to the tab; short enough that a store does not fill with abandoned files.
    /// </summary>
    internal static readonly TimeSpan MaxStagingAge = TimeSpan.FromHours(24);

    internal static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<StagingSweepWorker> _logger;
    private readonly IJobLease? _lease;

    public StagingSweepWorker(IServiceScopeFactory scopes, ILogger<StagingSweepWorker> logger, IJobLease? lease = null)
    {
        _lease = lease;
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // The wait comes first on purpose: an upload is confirmed within minutes of
            // arriving, so nothing is urgent at startup, and a start must not wait on
            // storage.
            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                // One sweep at a time in a cluster: the nodes share the store.
                await using var sweep = await _lease.AcquireAsync("staging:sweep", stoppingToken);
                if (sweep == null) continue;

                // Each repository stages under a prefix of its own, so each is swept in a scope pinned to it.
                IReadOnlyList<RepositoryInfo> repositories;
                using (var listing = _scopes.CreateScope())
                {
                    repositories = await listing.ServiceProvider
                        .GetRequiredService<IRepositoryService>()
                        .ListAsync(stoppingToken);
                }

                foreach (var repository in repositories)
                {
                    using var scope = _scopes.CreateRepositoryScope(repository);
                    var staging = scope.ServiceProvider.GetRequiredService<IPackageStagingService>();
                    var time = scope.ServiceProvider.GetRequiredService<SystemTime>();

                    await staging.SweepAsync(time.UtcNow - MaxStagingAge, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                // Retried on the next pass: a store that is briefly unreachable must not
                // stop the sweep for the life of the process.
                _logger.LogError(e, "Could not sweep abandoned staged uploads");
            }
        }
    }
}