using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaGet.Core;

/// <summary>A version waiting to be scanned.</summary>
/// <param name="Repository">The repository it is stored in.</param>
/// <param name="Name">The artifact name.</param>
/// <param name="Version">The version.</param>
public sealed record QueuedScan(RepositoryInfo Repository, string Name, string Version);

/// <summary>
/// Versions to scan soon (just stored by a deploy or a cache fill, or found unscanned by a download), in memory per node. The same
/// version is queued once; a full queue drops new entries (the scheduled sweep finds them).
/// </summary>
public sealed class ScanQueue
{
    private const int Capacity = 5000;

    private readonly Channel<QueuedScan> _channel = Channel.CreateBounded<QueuedScan>(new BoundedChannelOptions(Capacity)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    private readonly ConcurrentDictionary<(int, string, string), byte> _queued = new();

    internal ChannelReader<QueuedScan> Reader => _channel.Reader;

    /// <summary>Queues a version unless it is queued already.</summary>
    public void Enqueue(RepositoryInfo repository, string name, string version)
    {
        if (repository == null) throw new ArgumentNullException(nameof(repository));
        if (!_queued.TryAdd((repository.Key, name, version), 0)) return;
        if (!_channel.Writer.TryWrite(new QueuedScan(repository, name, version))) _queued.TryRemove((repository.Key, name, version), out _);
    }

    internal void Done(QueuedScan item) => _queued.TryRemove((item.Repository.Key, item.Name, item.Version), out _);

    /// <summary>How many versions are waiting (for tests and status).</summary>
    public int Pending => _queued.Count;
}

/// <summary>
/// Scans what <see cref="ScanQueue"/> holds and, every ten minutes, repositories whose results are older than
/// <see cref="ScanningOptions.RescanInterval"/> (new advisories appear after the first scan). Idle when scanning is off.
/// </summary>
public sealed class ScanWorker : BackgroundService
{
    private static readonly TimeSpan SweepEvery = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FirstSweepAfter = TimeSpan.FromMinutes(1);

    private readonly ScanQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ScanWorker> _logger;
    private readonly IJobLease? _lease;

    public ScanWorker(ScanQueue queue, IServiceScopeFactory scopes, ILogger<ScanWorker> logger, IJobLease? lease = null)
    {
        _lease = lease;
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextSweep = DateTime.UtcNow + FirstSweepAfter;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
                {
                    wait.CancelAfter(TimeSpan.FromSeconds(30));
                    try
                    {
                        await _queue.Reader.WaitToReadAsync(wait.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                    {
                        // Nothing queued for a while: time to look at the schedule.
                    }
                }

                while (_queue.Reader.TryRead(out var item))
                {
                    await ProcessAsync(item, stoppingToken).ConfigureAwait(false);
                }

                if (DateTime.UtcNow >= nextSweep)
                {
                    nextSweep = DateTime.UtcNow + SweepEvery;
                    await SweepAsync(stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Scanning worker iteration failed.");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task ProcessAsync(QueuedScan item, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopes.CreateRepositoryScope(item.Repository);
            var scans = scope.ServiceProvider.GetRequiredService<IScanService>();
            if (!scans.GetEngineStatus().Available) return;
            await scans.ScanAsync(item.Name, item.Version, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MaintenanceException)
        {
            // Read-only while a copy runs; the next sweep picks the version up.
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Scanning {Name} {Version} in {Repository} failed.", item.Name, item.Version, item.Repository.Name);
        }
        finally
        {
            _queue.Done(item);
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        // The scheduled rescan runs on one node of a cluster at a time (the queue of just-stored versions stays per node).
        await using var sweep = await _lease.AcquireAsync("scan:sweep", cancellationToken).ConfigureAwait(false);
        if (sweep == null) return;

        using var root = _scopes.CreateScope();
        var options = root.ServiceProvider.GetRequiredService<IOptionsSnapshot<ScanningOptions>>().Value;
        if (!options.Enabled || options.RescanInterval <= TimeSpan.Zero) return;
        if (root.ServiceProvider.GetRequiredService<IContext>() is NullContext) return;

        var repositories = await root.ServiceProvider.GetRequiredService<IRepositoryService>().ListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var repository in repositories.Where(r => r.Kind != RepositoryKind.Virtual))
        {
            try
            {
                using var scope = _scopes.CreateRepositoryScope(repository);
                var scans = scope.ServiceProvider.GetRequiredService<IScanService>();
                if (!scans.GetEngineStatus().Available) continue;
                var report = await scans.ScanRepositoryAsync(onlyStale: true, cancellationToken).ConfigureAwait(false);
                if (report.Scanned + report.Failed + report.Removed > 0)
                {
                    _logger.LogInformation("Scheduled scan of {Repository}: {Report}", repository.Name, report);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (MaintenanceException)
            {
                return;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Scheduled scan of {Repository} failed.", repository.Name);
            }
        }
    }
}

/// <summary>The <em>Scan repository</em> operation of the Operations page: scans every version now.</summary>
public sealed class ScanRepositoryOperation : IAdminOperation
{
    public string Name => "vulnerability-scan";

    public string Title => "Scan for vulnerabilities";

    public string Description => "Checks every artifact of the repository against the vulnerability database (OSV, or Trivy for container images) and stores the findings. Needs Scanning:Enabled.";

    public bool IsAvailable(IServiceProvider services)
        => services.GetRequiredService<IOptionsSnapshot<ScanningOptions>>().Value.Enabled
           && services.GetRequiredService<IContext>() is not NullContext;

    public bool AppliesTo(RepositoryInfo repository) => repository.Kind != RepositoryKind.Virtual;

    public async Task<string> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var report = await services.GetRequiredService<IScanService>().ScanRepositoryAsync(onlyStale: false, cancellationToken).ConfigureAwait(false);
        return report.ToString();
    }
}
