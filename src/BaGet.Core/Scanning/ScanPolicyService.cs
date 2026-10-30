using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core;

/// <summary>What a policy is attached to.</summary>
public enum ScanPolicyScope
{
    /// <summary>One repository.</summary>
    Repository,

    /// <summary>Every repository of a project that has no policy of its own.</summary>
    Project,
}

/// <summary>The thresholds of a policy; null disables the action.</summary>
/// <param name="WarnSeverity">Show a warning from this severity (0 low … 3 critical).</param>
/// <param name="BlockDownloadSeverity">Refuse downloads from this severity.</param>
/// <param name="BlockDeploySeverity">Refuse deploys from this severity.</param>
/// <param name="BlockUnscanned">Refuse downloads of versions that were not scanned.</param>
public sealed record ScanPolicyValues(int? WarnSeverity, int? BlockDownloadSeverity, int? BlockDeploySeverity, bool BlockUnscanned)
{
    /// <summary>Whether nothing is enabled.</summary>
    public bool IsEmpty => WarnSeverity == null && BlockDownloadSeverity == null && BlockDeploySeverity == null && !BlockUnscanned;

    /// <summary>The first problem with the thresholds, or null.</summary>
    public string? Validate()
    {
        foreach (var (label, value) in new[] { ("warn", WarnSeverity), ("block download", BlockDownloadSeverity), ("block deploy", BlockDeploySeverity) })
        {
            if (value != null && (value < 0 || value > PackageVulnerabilityRules.MaxSeverity))
            {
                return $"The {label} severity must be between 0 (low) and {PackageVulnerabilityRules.MaxSeverity} (critical).";
            }
        }

        return null;
    }
}

/// <summary>The policy in force for a repository: its own, else its project's, else none.</summary>
/// <param name="Values">The thresholds; <see cref="ScanPolicyValues.IsEmpty"/> when no policy applies.</param>
/// <param name="From">Where it comes from, or null when none applies.</param>
/// <param name="Name">The repository or project name it is stored for, or null.</param>
public sealed record EffectiveScanPolicy(ScanPolicyValues Values, ScanPolicyScope? From, string? Name)
{
    /// <summary>No policy.</summary>
    public static EffectiveScanPolicy None { get; } = new(new ScanPolicyValues(null, null, null, false), null, null);
}

/// <summary>Reads and edits the scanning policies. Scoped; authorizes nothing.</summary>
public interface IScanPolicyService
{
    /// <summary>The policy in force for the repository <paramref name="repositoryKey"/> (remembered for a few seconds).</summary>
    Task<EffectiveScanPolicy> GetEffectiveAsync(int repositoryKey, CancellationToken cancellationToken);

    /// <summary>The policy stored for a repository or project, or null.</summary>
    Task<ScanPolicy?> GetAsync(ScanPolicyScope scope, int key, CancellationToken cancellationToken);

    /// <summary>Saves the policy of a repository or project; empty values delete it. Audited; refused while the database is read-only.</summary>
    /// <exception cref="ArgumentException">A threshold is outside 0..3, or the repository or project does not exist.</exception>
    Task SaveAsync(ScanPolicyScope scope, int key, ScanPolicyValues values, string? actor, CancellationToken cancellationToken);
}

/// <summary>Remembers effective policies for a few seconds, so a download costs no query when a policy is in force or not.</summary>
public sealed class ScanPolicyCache
{
    private static readonly long Lifetime = 5_000;
    private readonly ConcurrentDictionary<int, (long Expires, EffectiveScanPolicy Policy)> _entries = new();

    internal bool TryGet(int repositoryKey, out EffectiveScanPolicy policy)
    {
        if (_entries.TryGetValue(repositoryKey, out var entry) && entry.Expires > Environment.TickCount64)
        {
            policy = entry.Policy;
            return true;
        }

        policy = EffectiveScanPolicy.None;
        return false;
    }

    internal void Set(int repositoryKey, EffectiveScanPolicy policy) => _entries[repositoryKey] = (Environment.TickCount64 + Lifetime, policy);

    /// <summary>Forgets everything (after a policy was saved on this node).</summary>
    public void Clear() => _entries.Clear();
}

/// <summary>The scoped <see cref="IScanPolicyService"/>.</summary>
public sealed class ScanPolicyService : IScanPolicyService
{
    private readonly IContext _context;
    private readonly ScanPolicyCache _cache;
    private readonly MaintenanceGate _gate;
    private readonly SystemTime _time;
    private readonly IAdminAudit _audit;

    public ScanPolicyService(IContext context, ScanPolicyCache cache, MaintenanceGate gate, SystemTime time, IAdminAudit audit)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    /// <inheritdoc/>
    public async Task<EffectiveScanPolicy> GetEffectiveAsync(int repositoryKey, CancellationToken cancellationToken)
    {
        if (_context is NullContext) return EffectiveScanPolicy.None;
        if (_cache.TryGet(repositoryKey, out var cached)) return cached;

        var repository = await _context.Repositories!.AsNoTracking().FirstOrDefaultAsync(r => r.Key == repositoryKey, cancellationToken).ConfigureAwait(false);
        var effective = EffectiveScanPolicy.None;
        if (repository != null)
        {
            var own = await _context.ScanPolicies!.AsNoTracking().FirstOrDefaultAsync(p => p.RepositoryKey == repositoryKey, cancellationToken).ConfigureAwait(false);
            if (own != null)
            {
                effective = new EffectiveScanPolicy(ToValues(own), ScanPolicyScope.Repository, repository.Name);
            }
            else if (repository.ProjectKey != null)
            {
                var projectKey = repository.ProjectKey.Value;
                var project = await _context.ScanPolicies!.AsNoTracking().FirstOrDefaultAsync(p => p.ProjectKey == projectKey, cancellationToken).ConfigureAwait(false);
                if (project != null)
                {
                    var projectName = await _context.Projects!.AsNoTracking().Where(p => p.Key == projectKey).Select(p => p.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                    effective = new EffectiveScanPolicy(ToValues(project), ScanPolicyScope.Project, projectName);
                }
            }
        }

        _cache.Set(repositoryKey, effective);
        return effective;
    }

    /// <inheritdoc/>
    public async Task<ScanPolicy?> GetAsync(ScanPolicyScope scope, int key, CancellationToken cancellationToken)
    {
        if (_context is NullContext) return null;
        return scope == ScanPolicyScope.Repository
            ? await _context.ScanPolicies!.AsNoTracking().FirstOrDefaultAsync(p => p.RepositoryKey == key, cancellationToken).ConfigureAwait(false)
            : await _context.ScanPolicies!.AsNoTracking().FirstOrDefaultAsync(p => p.ProjectKey == key, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SaveAsync(ScanPolicyScope scope, int key, ScanPolicyValues values, string? actor, CancellationToken cancellationToken)
    {
        if (values == null) throw new ArgumentNullException(nameof(values));
        _gate.ThrowIfClosed();
        var error = values.Validate();
        if (error != null) throw new ArgumentException(error, nameof(values));

        string subject;
        if (scope == ScanPolicyScope.Repository)
        {
            var name = await _context.Repositories!.AsNoTracking().Where(r => r.Key == key).Select(r => r.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (name == null) throw new ArgumentException("The repository does not exist.", nameof(key));
            subject = "repository " + name;
        }
        else
        {
            var name = await _context.Projects!.AsNoTracking().Where(p => p.Key == key).Select(p => p.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (name == null) throw new ArgumentException("The project does not exist.", nameof(key));
            subject = "project " + name;
        }

        var row = scope == ScanPolicyScope.Repository
            ? await _context.ScanPolicies!.FirstOrDefaultAsync(p => p.RepositoryKey == key, cancellationToken).ConfigureAwait(false)
            : await _context.ScanPolicies!.FirstOrDefaultAsync(p => p.ProjectKey == key, cancellationToken).ConfigureAwait(false);

        if (values.IsEmpty)
        {
            if (row != null)
            {
                _context.ScanPolicies!.Remove(row);
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            _cache.Clear();
            _audit.Record(actor, "scan-policy.delete", subject);
            return;
        }

        if (row == null)
        {
            row = new ScanPolicy();
            if (scope == ScanPolicyScope.Repository) row.RepositoryKey = key;
            else row.ProjectKey = key;
            _context.ScanPolicies!.Add(row);
        }

        row.WarnSeverity = values.WarnSeverity;
        row.BlockDownloadSeverity = values.BlockDownloadSeverity;
        row.BlockDeploySeverity = values.BlockDeploySeverity;
        row.BlockUnscanned = values.BlockUnscanned;
        row.Modified = _time.UtcNow;
        row.ModifiedBy = actor == null ? null : (actor.Length > AbstractContext<Microsoft.EntityFrameworkCore.DbContext>.MaxAccountNameLength ? actor.Substring(0, AbstractContext<Microsoft.EntityFrameworkCore.DbContext>.MaxAccountNameLength) : actor);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _cache.Clear();
        _audit.Record(actor, "scan-policy.save", subject,
            $"warn={Show(values.WarnSeverity)}, block download={Show(values.BlockDownloadSeverity)}, block deploy={Show(values.BlockDeploySeverity)}, block unscanned={values.BlockUnscanned}");
    }

    private static string Show(int? severity) => severity == null ? "off" : ScanSeverities.Name(severity.Value);

    private static ScanPolicyValues ToValues(ScanPolicy policy)
        => new(policy.WarnSeverity, policy.BlockDownloadSeverity, policy.BlockDeploySeverity, policy.BlockUnscanned);
}
