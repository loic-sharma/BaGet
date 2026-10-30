using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BaGet.Core;

/// <summary>A refusal the administrator can fix (invalid input, unknown repository, a disabled configuration); the message is shown on the page.</summary>
public sealed class ReplicationException : AccessModelException
{
    public ReplicationException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Creates the connection to a peer BaGet server. The credential is the peer's API key, sent as a bearer token to the peer's own
/// origin only (never to a redirect target); the transport is <see cref="IRemoteHttpClientFactory"/>, so <c>Remote:BlockPrivateAddresses</c>
/// applies to every connection and tests replace the transport in one place.
/// </summary>
public interface IReplicationPeerFactory
{
    /// <param name="peerUrl">The peer's base address.</param>
    /// <param name="peerRepository">The peer's repository.</param>
    /// <param name="apiKey">The peer's API key, or null for an anonymous peer.</param>
    /// <param name="timeoutSeconds">The time a request may take.</param>
    ReplicationPeer Create(string peerUrl, string peerRepository, string? apiKey, int timeoutSeconds = 600);
}

/// <summary>The default <see cref="IReplicationPeerFactory"/>.</summary>
public sealed class ReplicationPeerFactory : IReplicationPeerFactory
{
    private readonly IRemoteHttpClientFactory _http;

    public ReplicationPeerFactory(IRemoteHttpClientFactory http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc/>
    public ReplicationPeer Create(string peerUrl, string peerRepository, string? apiKey, int timeoutSeconds = 600)
    {
        // A password with no user name is sent as a bearer token to the URL's origin: exactly an API key.
        var settings = new RemoteRepositorySettings
        {
            Url = peerUrl,
            Protocol = RemoteProtocol.Npm,
            Password = string.IsNullOrEmpty(apiKey) ? null : apiKey,
            DownloadTimeoutSeconds = timeoutSeconds,
        };
        var uri = settings.UrlOrNull ?? throw new ReplicationException("The peer URL must be an absolute http or https address.");

        return new ReplicationPeer(_http.CreateProbe(settings), uri, peerRepository, ownsClient: true, apiKey: string.IsNullOrEmpty(apiKey) ? null : apiKey);
    }
}

/// <summary>
/// Runs one replication of a configuration: lists both sides, plans (<see cref="ReplicationPlanner"/>), copies what the target lacks
/// through the format's own protocol, applies the conflict rule, propagates deletes of what it copied, and writes the log
/// (<see cref="ReplicationRun"/>) and the ledger (<see cref="ReplicationItem"/>) as it goes. Because every run plans from what both
/// sides hold <i>now</i>, a run that was interrupted (the server stopped, the peer vanished, an administrator stopped it) is continued
/// by starting another: what was copied is skipped, what is left is copied.
/// </summary>
public sealed class ReplicationEngine
{
    /// <summary>The items between two saves of the run's progress.</summary>
    internal const int SaveEvery = 10;

    private const int MaxDetailLines = 40;

    private readonly IContext _context;
    private readonly IServiceScopeFactory _scopes;
    private readonly IRepositoryService _repositories;
    private readonly IReplicationPeerFactory _peers;
    private readonly RemoteSettingsCodec _codec;
    private readonly SystemTime _time;
    private readonly MaintenanceGate _gate;
    private readonly ILogger<ReplicationEngine> _logger;

    public ReplicationEngine(
        IContext context,
        IServiceScopeFactory scopes,
        IRepositoryService repositories,
        IReplicationPeerFactory peers,
        RemoteSettingsCodec codec,
        SystemTime time,
        MaintenanceGate gate,
        ILogger<ReplicationEngine> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _peers = peers ?? throw new ArgumentNullException(nameof(peers));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Test seam: called after each item with the number of items handled so far; throwing or cancelling here interrupts the run.</summary>
    internal Func<int, CancellationToken, Task>? AfterItem { get; set; }

    /// <summary>Runs the configuration to the end and returns its log entry (also stored).</summary>
    /// <param name="configKey">The configuration.</param>
    /// <param name="trigger"><c>manual</c>, <c>resume</c>, <c>schedule</c> or <c>deploy</c>.</param>
    /// <param name="actor">The administrator, or <c>scheduler</c>.</param>
    /// <param name="resumedFrom">The run this one continues, or null.</param>
    /// <param name="cancellationToken">Cancelling ends the run as <see cref="ReplicationRunStatus.Interrupted"/>.</param>
    /// <exception cref="ReplicationException">The configuration does not exist, is disabled, or its repository cannot take part.</exception>
    /// <exception cref="MaintenanceException">The server is read-only.</exception>
    public async Task<ReplicationRun> RunAsync(int configKey, string trigger, string? actor, int? resumedFrom, CancellationToken cancellationToken)
    {
        if (_context is NullContext) throw new ReplicationException("Replication needs a relational database.");
        _gate.ThrowIfClosed();

        var config = await _context.ReplicationConfigs!.FirstOrDefaultAsync(c => c.Key == configKey, cancellationToken).ConfigureAwait(false)
            ?? throw new ReplicationException("The replication no longer exists.");
        if (!config.Enabled) throw new ReplicationException($"The replication '{config.Name}' is disabled.");

        var repository = await _repositories.FindAsync(config.RepositoryKey, cancellationToken).ConfigureAwait(false)
            ?? throw new ReplicationException($"The repository of the replication '{config.Name}' no longer exists.");
        if (repository.Kind != RepositoryKind.Local) throw new ReplicationException($"The repository '{repository.Name}' is not a local repository.");

        var started = _time.UtcNow;
        var run = new ReplicationRun
        {
            ConfigKey = config.Key,
            Status = ReplicationRunStatus.Running,
            Trigger = trigger,
            Actor = actor,
            ResumedFrom = resumedFrom,
            Started = started,
        };
        _context.ReplicationRuns!.Add(run);
        config.LastRun = started;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var detail = new List<string>();
        try
        {
            await ExecuteAsync(config, repository, run, detail, cancellationToken).ConfigureAwait(false);
            run.Status = run.Failures > 0 ? ReplicationRunStatus.Failed : ReplicationRunStatus.Succeeded;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            run.Status = ReplicationRunStatus.Interrupted;
            run.Error = "Stopped before the end; start the replication again to continue.";
        }
        catch (MaintenanceException)
        {
            run.Status = ReplicationRunStatus.Interrupted;
            run.Error = "The server became read-only; start the replication again to continue.";
        }
        catch (Exception e) when (IsTransport(e))
        {
            run.Status = ReplicationRunStatus.Failed;
            run.Error = Truncate("The peer could not be reached: " + Safe(e), ReplicationLimits.MaxResultLength);
        }
        catch (ReplicationItemException e)
        {
            run.Status = ReplicationRunStatus.Failed;
            run.Error = Truncate(e.Message, ReplicationLimits.MaxResultLength);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Replication {Replication} failed", config.Name);
            run.Status = ReplicationRunStatus.Failed;
            run.Error = Truncate($"{e.GetType().Name}: {e.Message}", ReplicationLimits.MaxResultLength);
        }

        await FinishAsync(config, run, detail, repository).ConfigureAwait(false);

        return run;
    }

    private async Task FinishAsync(ReplicationConfig config, ReplicationRun run, List<string> detail, RepositoryInfo repository)
    {
        run.Finished = _time.UtcNow;
        run.Detail = detail.Count == 0 ? null : Truncate(string.Join("\n", detail), ReplicationLimits.MaxDetailLength);
        config.LastResult = Truncate(Summary(run), ReplicationLimits.MaxResultLength);
        AccessAdministration.Audit(_context, _time.UtcNow, run.Actor, "replication.run", config.Name, new
        {
            repository = repository.Name,
            run.Trigger,
            status = run.Status.ToString(),
            run.Transferred,
            run.Deleted,
            run.Conflicts,
            run.Failures,
        });

        // The run is over whatever stopped it: saving must not be cancelled with it.
        await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>The one-line account of a run, as the list shows it.</summary>
    public static string Summary(ReplicationRun run)
    {
        var text = $"{run.Status}: {run.Transferred} copied, {run.Skipped} already there, {run.Deleted} deleted, {run.Conflicts} conflicts, {run.Failures} failed";

        return string.IsNullOrEmpty(run.Error) ? text : text + ". " + run.Error;
    }

    private async Task ExecuteAsync(ReplicationConfig config, RepositoryInfo repository, ReplicationRun run, List<string> detail, CancellationToken cancellationToken)
    {
        string? apiKey = null;
        if (!string.IsNullOrEmpty(config.ProtectedApiKey))
        {
            apiKey = _codec.TryUnprotectSecret(config.ProtectedApiKey)
                ?? throw new ReplicationItemException("The stored API key cannot be read (the data protection keys changed); enter it again.");
        }

        using var peer = _peers.Create(config.PeerUrl!, config.PeerRepository!, apiKey);
        using var scope = _scopes.CreateRepositoryScope(repository);
        var replicator = scope.ServiceProvider.GetFormatService<IFormatReplicator>(repository.Format)
            ?? throw new ReplicationItemException($"Replication is not available for {repository.Format} repositories.");

        var push = config.Direction == ReplicationDirection.Push;
        var inventory = await peer.GetInventoryAsync(summary: false, cancellationToken).ConfigureAwait(false);
        if (inventory.Format != repository.Format)
        {
            throw new ReplicationItemException($"The peer's repository '{peer.Repository}' is a {inventory.Format} repository; '{repository.Name}' is {repository.Format}.");
        }

        if (push && inventory.Kind != RepositoryKind.Local)
        {
            throw new ReplicationItemException($"The peer's repository '{peer.Repository}' is {inventory.Kind.ToString().ToLowerInvariant()}, so it cannot receive.");
        }

        var local = await replicator.ListAsync(cancellationToken).ConfigureAwait(false);
        var names = replicator.NameComparer;
        var ledgerRows = await _context.ReplicationItems!.Where(i => i.ConfigKey == config.Key).ToListAsync(cancellationToken).ConfigureAwait(false);
        var ledger = new Dictionary<ReplicaItem, ReplicationItem>(ReplicationPlanner.KeyComparer(names));
        foreach (var row in ledgerRows) ledger[new ReplicaItem(row.Name!, row.Version!, row.Part ?? string.Empty, 0, row.Digest ?? string.Empty)] = row;

        var plan = ReplicationPlanner.Plan(
            push ? local : inventory.Items,
            push ? inventory.Items : local,
            ledger.Keys.ToList(),
            names,
            config.Conflict,
            config.PropagateDeletes);
        run.Examined = plan.Examined;
        run.Skipped = plan.Skipped;

        var handled = 0;
        async Task StepAsync()
        {
            handled++;
            if (handled % SaveEvery == 0) await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (AfterItem != null) await AfterItem(handled, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        foreach (var item in plan.Conflicts)
        {
            run.Conflicts++;
            AddDetail(detail, $"conflict: {item} is held differently on both sides; the {(push ? "peer" : "local")} copy was kept");
        }

        foreach (var item in plan.Replace)
        {
            run.Conflicts++;
            AddDetail(detail, $"conflict: {item} is held differently on both sides; the {(push ? "peer" : "local")} copy is replaced");

            // A pull replaces the local copy only once the peer's item is fetched and accepted (PullReplacingAsync), so a refusal
            // (scanning policy, integrity) or a transport failure keeps what was there. A push moves a Docker tag in place and
            // deletes at the peer first for the formats whose peers refuse to overwrite (IFormatReplicator.PushReplacesInPlace).
            if (push && !replicator.PushReplacesInPlace && !await TryAsync(item, "delete", () => DeleteTargetAsync(replicator, peer, push, item, cancellationToken), run, detail).ConfigureAwait(false)) continue;

            await CopyAsync(config, replicator, peer, push, item, ledger, run, detail, cancellationToken, replace: true).ConfigureAwait(false);
            await StepAsync().ConfigureAwait(false);
        }

        foreach (var item in plan.Copy)
        {
            await CopyAsync(config, replicator, peer, push, item, ledger, run, detail, cancellationToken).ConfigureAwait(false);
            await StepAsync().ConfigureAwait(false);
        }

        foreach (var item in plan.Delete)
        {
            if (await TryAsync(item, "delete", () => DeleteTargetAsync(replicator, peer, push, item, cancellationToken), run, detail).ConfigureAwait(false))
            {
                run.Deleted++;
                Forget(ledger, item);
            }

            await StepAsync().ConfigureAwait(false);
        }

        foreach (var item in plan.Forget) Forget(ledger, item);
    }

    private static Task DeleteTargetAsync(IFormatReplicator replicator, ReplicationPeer peer, bool push, ReplicaItem item, CancellationToken cancellationToken)
        => push ? replicator.DeletePeerAsync(item, peer, cancellationToken) : replicator.DeleteLocalAsync(item, cancellationToken);

    private async Task CopyAsync(
        ReplicationConfig config,
        IFormatReplicator replicator,
        ReplicationPeer peer,
        bool push,
        ReplicaItem item,
        Dictionary<ReplicaItem, ReplicationItem> ledger,
        ReplicationRun run,
        List<string> detail,
        CancellationToken cancellationToken,
        bool replace = false)
    {
        long bytes = 0;
        if (!await TryAsync(item, push ? "push" : "pull", async () => bytes = push
                ? await replicator.PushAsync(item, peer, cancellationToken).ConfigureAwait(false)
                : replace
                    ? await replicator.PullReplacingAsync(item, peer, cancellationToken).ConfigureAwait(false)
                    : await replicator.PullAsync(item, peer, cancellationToken).ConfigureAwait(false), run, detail).ConfigureAwait(false))
        {
            return;
        }

        run.Transferred++;
        run.Bytes += bytes;
        if (!ledger.TryGetValue(item, out var row))
        {
            row = new ReplicationItem { ConfigKey = config.Key, Name = item.Name, Version = item.Version, Part = item.Part };
            _context.ReplicationItems!.Add(row);
            ledger[item] = row;
        }

        row.Digest = item.Digest;
        row.Synced = _time.UtcNow;
    }

    private void Forget(Dictionary<ReplicaItem, ReplicationItem> ledger, ReplicaItem item)
    {
        if (!ledger.TryGetValue(item, out var row)) return;

        ledger.Remove(item);
        _context.ReplicationItems!.Remove(row); // an added row (never saved) is simply detached
    }

    // One item's failure is the item's: logged, counted, and the run goes on. A transport failure, a stop and a read-only server
    // end the run (they would fail every remaining item the same way).
    private async Task<bool> TryAsync(ReplicaItem item, string what, Func<Task> action, ReplicationRun run, List<string> detail)
    {
        try
        {
            await action().ConfigureAwait(false);

            return true;
        }
        catch (ReplicationItemException e)
        {
            if (e.Conflict)
            {
                run.Conflicts++;
                AddDetail(detail, $"conflict: {item}: {e.Message}");
            }
            else
            {
                run.Failures++;
                AddDetail(detail, $"failed ({what}): {item}: {e.Message}");
            }

            return false;
        }
        catch (Exception e) when (e is not OperationCanceledException and not MaintenanceException && !IsTransport(e))
        {
            run.Failures++;
            AddDetail(detail, $"failed ({what}): {item}: {e.GetType().Name}: {Safe(e)}");
            _logger.LogWarning(e, "Replication of {Item} failed", item);

            return false;
        }
    }

    // HttpClient wraps connection and socket failures in HttpRequestException; a timeout is a TaskCanceledException the run's own
    // token did not cause.
    private static bool IsTransport(Exception e)
        => e is HttpRequestException or TaskCanceledException or TimeoutException
           || e.InnerException is HttpRequestException;

    // Transport messages can name the URL; never a credential (the key travels in a header), but cut anyway.
    private static string Safe(Exception e) => Truncate(e.GetBaseException().Message, 300);

    private static void AddDetail(List<string> detail, string line)
    {
        if (detail.Count < MaxDetailLines) detail.Add(Truncate(line, 300));
        else if (detail.Count == MaxDetailLines) detail.Add("... more entries are not listed");
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text.Substring(0, length - 3) + "...";
}
