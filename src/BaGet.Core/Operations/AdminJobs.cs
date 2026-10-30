using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BaGet.Core;

/// <summary>
/// A long-running maintenance operation an administrator can start from the web UI for one repository
/// (what the <c>catalog rebuild</c> and <c>import downloads</c> commands do). Registered as a singleton.
/// </summary>
public interface IAdminOperation
{
    /// <summary>The stable id used in forms and status (<c>catalog-rebuild</c>).</summary>
    string Name { get; }

    string Title { get; }

    string Description { get; }

    /// <summary>Whether the configured services can run it; <paramref name="services"/> is a request scope.</summary>
    bool IsAvailable(IServiceProvider services);

    /// <summary>
    /// Whether the operation makes sense for <paramref name="repository"/> (a Docker garbage collection does not for a NuGet repository).
    /// The Operations page lists it only for those, and <see cref="AdminJobService.TryStart"/> refuses the others. Default: every repository.
    /// </summary>
    bool AppliesTo(RepositoryInfo repository) => true;

    /// <summary>
    /// Whether the operation deletes data, so the Operations page asks the administrator's password again before it starts it (as the
    /// other destructive pages do). Default: no.
    /// </summary>
    bool RequiresConfirmation => false;

    /// <summary>Runs it inside a scope already pinned to the repository.</summary>
    /// <returns>A short human-readable result, such as the counts.</returns>
    Task<string> RunAsync(IServiceProvider services, CancellationToken cancellationToken);
}

public enum AdminJobState
{
    Running,
    Succeeded,
    Failed,
}

/// <summary>The last run of an operation on a repository. In memory: a restart forgets it.</summary>
public sealed class AdminJobStatus
{
    public string Operation { get; init; } = string.Empty;

    public string Repository { get; init; } = string.Empty;

    public AdminJobState State { get; internal set; }

    public string? Actor { get; init; }

    public DateTime Started { get; init; }

    public DateTime? Finished { get; internal set; }

    /// <summary>The result on success, the exception type and message on failure.</summary>
    public string? Message { get; internal set; }
}

/// <summary>
/// Runs <see cref="IAdminOperation"/>s in the background, one at a time per operation and repository, and keeps
/// the last status of each. Starting is refused while the <see cref="MaintenanceGate"/> is closed (throws
/// <see cref="MaintenanceException"/>), and every start and outcome is recorded with <see cref="IAdminAudit"/>.
/// </summary>
public sealed class AdminJobService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IEnumerable<IAdminOperation> _operations;
    private readonly MaintenanceGate? _gate;
    private readonly IAdminAudit _audit;
    private readonly SystemTime _time;
    private readonly ILogger<AdminJobService> _logger;
    private readonly IHostApplicationLifetime? _lifetime;
    private readonly IJobLease? _lease;
    private readonly ConcurrentDictionary<(string, string), AdminJobStatus> _status = new();
    private readonly ConcurrentDictionary<(string, string), Task> _running = new();
    private readonly object _lock = new();

    public AdminJobService(
        IServiceScopeFactory scopes,
        IEnumerable<IAdminOperation> operations,
        IAdminAudit audit,
        SystemTime time,
        ILogger<AdminJobService> logger,
        MaintenanceGate? gate = null,
        IHostApplicationLifetime? lifetime = null,
        IJobLease? lease = null)
    {
        _lease = lease;
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gate = gate;
        _lifetime = lifetime;
    }

    /// <summary>Every registered operation.</summary>
    public IReadOnlyList<IAdminOperation> Operations => _operations.ToList();

    /// <summary>The last status of <paramref name="operation"/> on <paramref name="repository"/>, or null if never run.</summary>
    public AdminJobStatus? Get(string operation, string repository)
        => _status.TryGetValue(Key(operation, repository), out var status) ? status : null;

    /// <summary>
    /// Starts <paramref name="operation"/> on <paramref name="repository"/> in the background.
    /// </summary>
    /// <param name="operation">The <see cref="IAdminOperation.Name"/>.</param>
    /// <param name="repository">The repository to run on.</param>
    /// <param name="actor">The administrator starting it, for the audit record.</param>
    /// <param name="refusal">Why it was not started.</param>
    /// <exception cref="MaintenanceException">The gate is closed.</exception>
    public bool TryStart(string operation, RepositoryInfo repository, string? actor, out string? refusal)
    {
        if (repository == null) throw new ArgumentNullException(nameof(repository));
        _gate?.ThrowIfClosed();

        var definition = _operations.FirstOrDefault(o => string.Equals(o.Name, operation, StringComparison.Ordinal));
        if (definition == null)
        {
            refusal = "Unknown operation.";
            return false;
        }

        if (!definition.AppliesTo(repository))
        {
            refusal = $"{definition.Title} does not apply to {repository.Name}.";
            return false;
        }

        var key = Key(operation, repository.Name);

        // One run per operation and repository in the whole cluster, not only in this process.
        var held = _lease.Acquire($"operation:{operation}:{key.Item2}");
        if (held == null)
        {
            refusal = $"{definition.Title} is already running on {repository.Name} (on another node).";
            return false;
        }

        var status = new AdminJobStatus
        {
            Operation = definition.Name,
            Repository = repository.Name,
            State = AdminJobState.Running,
            Actor = actor,
            Started = _time.UtcNow,
        };
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            if (_running.ContainsKey(key))
            {
                held.Release();
                refusal = $"{definition.Title} is already running on {repository.Name}.";
                return false;
            }

            _status[key] = status;
            _running[key] = completion.Task;
        }

        _audit.Record(actor, $"operation.{definition.Name}.start", $"repository {repository.Name}");
        var stopping = _lifetime?.ApplicationStopping ?? CancellationToken.None;
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopes.CreateRepositoryScope(repository);
                var message = await definition.RunAsync(scope.ServiceProvider, stopping);
                Finish(status, AdminJobState.Succeeded, message);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "{Operation} on repository {Repository} failed", definition.Name, repository.Name);
                Finish(status, AdminJobState.Failed, $"{e.GetType().Name}: {e.Message}");
            }
            finally
            {
                _running.TryRemove(key, out _);
                try { await held.DisposeAsync(); } catch (Exception) { /* the lease expires by itself */ }
                completion.SetResult(true);
            }

            _audit.Record(actor, $"operation.{definition.Name}.{status.State.ToString().ToLowerInvariant()}", $"repository {repository.Name}", status.Message);
        });

        refusal = null;
        return true;
    }

    /// <summary>Completes when the current run of <paramref name="operation"/> on <paramref name="repository"/> ends (immediately when none).</summary>
    public Task WaitAsync(string operation, string repository)
        => _running.TryGetValue(Key(operation, repository), out var task) ? task : Task.CompletedTask;

    private void Finish(AdminJobStatus status, AdminJobState state, string? message)
    {
        status.Message = message;
        status.Finished = _time.UtcNow;
        status.State = state;
    }

    private static (string, string) Key(string operation, string repository)
        => (operation, repository.ToLowerInvariant());
}
