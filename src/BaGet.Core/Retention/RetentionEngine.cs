using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BaGet.Core;

/// <summary>
/// Runs a <see cref="RetentionPolicy"/> over its repositories. One code path computes the report and deletes: the dry run stops after
/// planning, the real run deletes exactly the planned versions (re-checking the legal holds just before each deletion, so a hold
/// placed meanwhile is honoured). Each repository is processed in its own scope pinned to it, through the format's
/// <see cref="IFormatRetentionHooks"/>.
/// </summary>
public sealed class RetentionEngine
{
    private readonly IContext _context;
    private readonly IServiceScopeFactory _scopes;
    private readonly SystemTime _time;
    private readonly MaintenanceGate _gate;
    private readonly IAdminAudit _audit;
    private readonly ILogger<RetentionEngine> _logger;
    private readonly ArtifactUsageRecorder? _usage;

    public RetentionEngine(
        IContext context,
        IServiceScopeFactory scopes,
        SystemTime time,
        MaintenanceGate gate,
        IAdminAudit audit,
        ILogger<RetentionEngine> logger,
        ArtifactUsageRecorder? usage = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _usage = usage;
    }

    /// <summary>
    /// The repositories <paramref name="policy"/> applies to: its repository, or every repository of its project. Virtual repositories
    /// store nothing and are left out.
    /// </summary>
    public async Task<IReadOnlyList<RepositoryInfo>> TargetsAsync(RetentionPolicy policy, CancellationToken cancellationToken)
    {
        if (policy == null) throw new ArgumentNullException(nameof(policy));

        var query = _context.Repositories!.AsNoTracking().Where(r => r.Kind != RepositoryKind.Virtual);
        query = policy.RepositoryKey is int repository
            ? query.Where(r => r.Key == repository)
            : policy.ProjectKey is int project ? query.Where(r => r.ProjectKey == project) : query.Where(r => false);

        return (await query.OrderBy(r => r.Name).ToListAsync(cancellationToken).ConfigureAwait(false)).Select(r => r.ToInfo()).ToList();
    }

    /// <summary>
    /// Plans the policy and, unless <paramref name="dryRun"/>, deletes what it selected.
    /// </summary>
    /// <param name="policyKey">The policy.</param>
    /// <param name="dryRun">Whether to only report.</param>
    /// <param name="actor">Who asked (the user name, or <c>scheduler</c>), for the audit trail.</param>
    /// <param name="cancellationToken">A token to cancel the run.</param>
    /// <exception cref="InvalidOperationException">There is no such policy, or a real run of a disabled one.</exception>
    /// <exception cref="MaintenanceException">A real run while the server is read-only, or one the gate closed on; the deletions already made are audited first.</exception>
    public async Task<RetentionReport> RunAsync(int policyKey, bool dryRun, string? actor, CancellationToken cancellationToken)
    {
        if (_context is NullContext) throw new InvalidOperationException("Retention needs a relational database.");

        var policy = await _context.RetentionPolicies!.AsNoTracking().FirstOrDefaultAsync(p => p.Key == policyKey, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("There is no such retention policy.");
        if (!dryRun)
        {
            if (!policy.Enabled) throw new InvalidOperationException($"Retention policy '{policy.Name}' is disabled.");

            _gate.ThrowIfClosed();
        }

        var started = _time.UtcNow;
        var report = new RetentionReport { PolicyKey = policy.Key, PolicyName = policy.Name ?? string.Empty, DryRun = dryRun, Started = started };

        // Downloads recorded in memory so far count for the "unused" rules.
        if (_usage != null) await _usage.FlushAsync(_scopes, cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (var repository in await TargetsAsync(policy, cancellationToken).ConfigureAwait(false))
            {
                var part = new RetentionRepositoryReport { Repository = repository.Name, Format = repository.Format };
                report.Repositories.Add(part);
                try
                {
                    await RunRepositoryAsync(policy, repository, dryRun, part, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException and not MaintenanceException)
                {
                    _logger.LogError(e, "Retention policy {Policy} failed on repository {Repository}", policy.Name, repository.Name);
                    part.Error = $"{e.GetType().Name}: {e.Message}";
                }
            }
        }
        catch (MaintenanceException) when (!dryRun)
        {
            // The gate closed mid-run: what was already deleted must not go unaudited. The bookkeeping row may be unwritable now
            // (RecordRunAsync tolerates that); the refusal still propagates.
            report.Finished = _time.UtcNow;
            _audit.Record(actor, "retention.run", $"policy {policy.Name}", "Interrupted by the read-only gate. " + report.Summary);
            await RecordRunAsync(policy, started, report, actor, cancellationToken).ConfigureAwait(false);

            throw;
        }

        report.Finished = _time.UtcNow;
        _audit.Record(actor, dryRun ? "retention.dry-run" : "retention.run", $"policy {policy.Name}", report.Summary);
        if (!dryRun) await RecordRunAsync(policy, started, report, actor, cancellationToken).ConfigureAwait(false);

        return report;
    }

    private async Task RunRepositoryAsync(RetentionPolicy policy, RepositoryInfo repository, bool dryRun, RetentionRepositoryReport part, CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateRepositoryScope(repository);
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<IContext>();
        var hooks = services.GetFormatService<IFormatRetentionHooks>(repository.Format);
        if (hooks == null)
        {
            part.Notes.Add($"The {repository.Format} format has no retention hooks; nothing was examined.");

            return;
        }

        var versions = await hooks.ListVersionsAsync(cancellationToken).ConfigureAwait(false);
        var unreferenced = policy.UntaggedAfterDays == null || repository.Format != RepositoryFormat.Docker
            ? Array.Empty<FormatVersion>()
            : await hooks.ListUnreferencedAsync(cancellationToken).ConfigureAwait(false);

        // The cache limit needs sizes; the listing leaves the costly ones out.
        if (policy.CacheLimitBytes != null && repository.Kind == RepositoryKind.Remote)
        {
            var sized = new List<FormatVersion>(versions.Count);
            foreach (var version in versions)
            {
                sized.Add(version.Size != null ? version : version with { Size = await hooks.GetSizeAsync(version.Name, version.Version, cancellationToken).ConfigureAwait(false) });
            }

            versions = sized;
        }

        var holds = await LoadHoldsAsync(context, cancellationToken).ConfigureAwait(false);
        var now = _time.UtcNow;
        var decision = RetentionEvaluator.Evaluate(policy, repository, versions, unreferenced, holds, now);

        part.Examined = decision.Examined;
        part.Notes.AddRange(decision.Notes);
        part.Selected = decision.Selected.Count;
        part.Held = decision.Held.Count;

        foreach (var held in decision.Held) part.Add(Item(repository, held, RetentionOutcome.Held, null));

        foreach (var item in decision.Selected)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (dryRun)
            {
                part.Bytes += item.Size ?? 0;
                part.Add(Item(repository, item, RetentionOutcome.Planned, null));
                continue;
            }

            // A hold placed since the plan was made is honoured.
            var current = await LoadHoldsAsync(context, cancellationToken).ConfigureAwait(false);
            if (current.IsHeld(item.Name, item.Version))
            {
                part.Held++;
                part.Selected--;
                part.Add(Item(repository, item, RetentionOutcome.Held, null));
                continue;
            }

            try
            {
                await hooks.DeleteVersionAsync(item.Name, item.Version, cancellationToken).ConfigureAwait(false);
                part.Deleted++;
                part.Bytes += item.Size ?? 0;
                part.Add(Item(repository, item, RetentionOutcome.Deleted, null));
                await ForgetUsageAsync(context, item, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException and not MaintenanceException)
            {
                _logger.LogWarning(e, "Retention could not delete {Name} {Version} from {Repository}", item.Name, item.Version, repository.Name);
                part.Failed++;
                part.Add(Item(repository, item, RetentionOutcome.Failed, $"{e.GetType().Name}: {e.Message}"));
            }
        }

        if (policy.CollectGarbage)
        {
            if (repository.Format != RepositoryFormat.Docker)
            {
                part.Notes.Add("The garbage collection applies to Docker repositories only; it was ignored.");
            }
            else if (!StreamingStorage.IsSupported(services.GetRequiredService<IStorageService>()))
            {
                part.Notes.Add("The configured storage cannot hold Docker blobs, so there is nothing to collect.");
            }
            else
            {
                part.GarbageCollection = await services.GetRequiredService<IOciGarbageCollector>().RunAsync(dryRun, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static RetentionReportItem Item(RepositoryInfo repository, RetentionSelection s, RetentionOutcome outcome, string? error)
        => new(repository.Name, s.Name, s.Version, s.Published, s.LastDownloaded, s.Size, string.Join(", ", s.Rules.Select(Describe)), outcome, error);

    /// <summary>The rule in words, for reports.</summary>
    public static string Describe(RetentionRule rule) => rule switch
    {
        RetentionRule.KeepLast => "beyond the newest versions kept",
        RetentionRule.OlderThan => "older than the age limit",
        RetentionRule.Unused => "not downloaded recently",
        RetentionRule.CacheLimit => "evicted from the cache (least recently used)",
        RetentionRule.Untagged => "untagged manifest",
        _ => rule.ToString(),
    };

    private static async Task<RetentionEvaluator.HoldSet> LoadHoldsAsync(IContext context, CancellationToken cancellationToken)
    {
        var rows = await context.RetentionHolds!.AsNoTracking().Select(h => new { h.Name, h.Version }).ToListAsync(cancellationToken).ConfigureAwait(false);

        return new RetentionEvaluator.HoldSet(rows.Select(r => (r.Name!, r.Version ?? string.Empty)));
    }

    // The download time of a version that is gone is of no use; a failure here must not undo the deletion.
    private async Task ForgetUsageAsync(IContext context, RetentionSelection item, CancellationToken cancellationToken)
    {
        try
        {
            var name = ArtifactUsageRecorder.Normalize(item.Name);
            var version = ArtifactUsageRecorder.Normalize(item.Version);
            var rows = await context.ArtifactUsages!.Where(u => u.Name == name && u.Version == version).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (rows.Count == 0) return;

            context.ArtifactUsages!.RemoveRange(rows);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException and not MaintenanceException)
        {
            _logger.LogDebug(e, "Could not drop the download time of {Name} {Version}", item.Name, item.Version);
        }
    }

    private async Task RecordRunAsync(RetentionPolicy policy, DateTime started, RetentionReport report, string? actor, CancellationToken cancellationToken)
    {
        try
        {
            var row = await _context.RetentionPolicies!.FirstOrDefaultAsync(p => p.Key == policy.Key, cancellationToken).ConfigureAwait(false);
            if (row == null) return;

            row.LastRun = started;
            var summary = report.Summary;
            row.LastResult = summary.Length <= RetentionLimits.MaxResultLength ? summary : summary.Substring(0, RetentionLimits.MaxResultLength - 3) + "...";
            AccessAdministration.Audit(_context, _time.UtcNow, actor, "retention.run", policy.Name, new
            {
                repositories = report.Repositories.Select(r => r.Repository).ToArray(),
                deleted = report.Deleted,
                held = report.Held,
                failed = report.Failed,
                bytes = report.Bytes,
            });
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The deletions are done; a gate that closed since only costs the bookkeeping, and the next run records itself.
            _logger.LogWarning(e, "Could not record the run of retention policy {Policy}", policy.Name);
        }
    }
}
