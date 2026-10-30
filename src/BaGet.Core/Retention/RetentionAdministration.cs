using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core;

/// <summary>A refusal the administrator can fix (invalid input, unknown repository); the message is shown on the page.</summary>
public sealed class RetentionException : AccessModelException
{
    public RetentionException(string message)
        : base(message)
    {
    }
}

/// <summary>What the administrator types to create or change a policy. <see cref="Key"/> 0 creates.</summary>
public sealed record RetentionPolicyInput(
    int Key,
    string? Name,
    string? Repository,
    string? Project,
    bool Enabled,
    string? NamePatterns,
    int? KeepLast,
    int? OlderThanDays,
    int? UnusedDays,
    bool PrereleaseOnly,
    long? CacheLimitBytes,
    int? UntaggedAfterDays,
    bool CollectGarbage,
    int? ScheduleHours);

/// <summary>A policy with the name of what it applies to.</summary>
/// <param name="Policy">The policy.</param>
/// <param name="Repository">The repository name, when it applies to one.</param>
/// <param name="Project">The project name, when it applies to a project.</param>
/// <param name="Targets">How many repositories it currently applies to.</param>
public sealed record RetentionPolicyView(RetentionPolicy Policy, string? Repository, string? Project, int Targets);

/// <summary>A legal hold with its repository's name.</summary>
public sealed record RetentionHoldView(int Key, int RepositoryKey, string Repository, string Name, string Version, string? Reason, string? CreatedBy, DateTime Created);

/// <summary>
/// Manages retention policies and legal holds. Every change calls <see cref="MaintenanceGate.ThrowIfClosed"/> and is audited
/// (<see cref="AccessAuditEntry"/>, in the same save as the change). Authorization is the caller's (the administrator pages).
/// </summary>
public interface IRetentionAdministration
{
    /// <summary>Every policy, by name.</summary>
    Task<IReadOnlyList<RetentionPolicyView>> ListPoliciesAsync(CancellationToken cancellationToken);

    /// <summary>One policy, or null.</summary>
    Task<RetentionPolicyView?> GetPolicyAsync(int key, CancellationToken cancellationToken);

    /// <summary>Creates (<see cref="RetentionPolicyInput.Key"/> 0) or changes a policy.</summary>
    /// <exception cref="RetentionException">The input is not valid.</exception>
    Task<RetentionPolicy> SavePolicyAsync(RetentionPolicyInput input, string? actor, CancellationToken cancellationToken);

    /// <summary>Deletes a policy (what it already deleted stays deleted).</summary>
    Task<bool> DeletePolicyAsync(int key, string? actor, CancellationToken cancellationToken);

    /// <summary>The legal holds of one repository, or of all when <paramref name="repository"/> is null.</summary>
    Task<IReadOnlyList<RetentionHoldView>> ListHoldsAsync(string? repository, CancellationToken cancellationToken);

    /// <summary>
    /// Places a hold on an artifact (<paramref name="version"/> empty: every version) of a repository. The name and version are put in
    /// the format's canonical form. Placing one that exists changes nothing.
    /// </summary>
    /// <exception cref="RetentionException">The repository is unknown or virtual, or the name or version is not valid for its format.</exception>
    Task<RetentionHold> AddHoldAsync(string repository, string name, string? version, string? reason, string? actor, CancellationToken cancellationToken);

    /// <summary>Lifts a hold.</summary>
    Task<bool> RemoveHoldAsync(int holdKey, string? actor, CancellationToken cancellationToken);
}

/// <summary><see cref="IRetentionAdministration"/> over the relational database.</summary>
public sealed class RetentionAdministration : IRetentionAdministration
{
    private readonly IContext _context;
    private readonly IServiceProvider _services;
    private readonly SystemTime _time;
    private readonly MaintenanceGate _gate;

    public RetentionAdministration(IContext context, IServiceProvider services, SystemTime time, MaintenanceGate gate)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    private IContext Db => _context is NullContext ? throw new RetentionException("Retention needs a relational database.") : _context;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RetentionPolicyView>> ListPoliciesAsync(CancellationToken cancellationToken)
    {
        var policies = await Db.RetentionPolicies!.AsNoTracking().OrderBy(p => p.Name).ToListAsync(cancellationToken).ConfigureAwait(false);

        return await ViewsAsync(policies, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<RetentionPolicyView?> GetPolicyAsync(int key, CancellationToken cancellationToken)
    {
        var policy = await Db.RetentionPolicies!.AsNoTracking().FirstOrDefaultAsync(p => p.Key == key, cancellationToken).ConfigureAwait(false);

        return policy == null ? null : (await ViewsAsync(new[] { policy }, cancellationToken).ConfigureAwait(false)).Single();
    }

    private async Task<IReadOnlyList<RetentionPolicyView>> ViewsAsync(IReadOnlyList<RetentionPolicy> policies, CancellationToken cancellationToken)
    {
        var repositories = await Db.Repositories!.AsNoTracking().Select(r => new { r.Key, r.Name, r.ProjectKey, r.Kind }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var projects = await Db.Projects!.AsNoTracking().ToDictionaryAsync(p => p.Key, p => p.Name!, cancellationToken).ConfigureAwait(false);

        return policies.Select(p =>
        {
            var targets = repositories.Count(r => r.Kind != RepositoryKind.Virtual
                && (p.RepositoryKey != null ? r.Key == p.RepositoryKey : p.ProjectKey != null && r.ProjectKey == p.ProjectKey));

            return new RetentionPolicyView(
                p,
                p.RepositoryKey is int rk ? repositories.FirstOrDefault(r => r.Key == rk)?.Name : null,
                p.ProjectKey is int pk ? (projects.TryGetValue(pk, out var name) ? name : null) : null,
                targets);
        }).ToList();
    }

    /// <inheritdoc/>
    public async Task<RetentionPolicy> SavePolicyAsync(RetentionPolicyInput input, string? actor, CancellationToken cancellationToken)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        var db = Db;
        _gate.ThrowIfClosed();

        var name = input.Name?.Trim();
        if (!AccessNames.IsValid(name, out var nameError)) throw new RetentionException(nameError ?? "The name is not valid.");

        var hasRepository = !string.IsNullOrWhiteSpace(input.Repository);
        var hasProject = !string.IsNullOrWhiteSpace(input.Project);
        if (hasRepository == hasProject) throw new RetentionException("Choose either one repository or one project.");

        Check(input.KeepLast, 1, 1_000_000, "Keep the newest");
        Check(input.OlderThanDays, 1, 36_500, "Older than (days)");
        Check(input.UnusedDays, 1, 36_500, "Unused for (days)");
        Check(input.UntaggedAfterDays, 0, 36_500, "Untagged for (days)");
        Check(input.ScheduleHours, 1, 8_760, "Repeat every (hours)");
        if (input.CacheLimitBytes is long cache && cache < 1) throw new RetentionException("The cache limit must be at least 1 byte.");

        var patterns = AccessPatterns.Split(input.NamePatterns);
        var patternText = patterns.Length == 0 ? null : string.Join("\n", patterns);
        if (patternText != null && patternText.Length > RetentionLimits.MaxPatternsLength) throw new RetentionException("The name patterns are too long.");

        if (input.KeepLast == null && input.OlderThanDays == null && input.UnusedDays == null && input.CacheLimitBytes == null
            && input.UntaggedAfterDays == null && !input.CollectGarbage)
        {
            throw new RetentionException("Choose at least one rule.");
        }

        int? repositoryKey = null;
        int? projectKey = null;
        if (hasRepository)
        {
            var repository = await db.Repositories!.AsNoTracking().FirstOrDefaultAsync(r => r.Name == input.Repository!.Trim(), cancellationToken).ConfigureAwait(false)
                ?? throw new RetentionException($"There is no repository '{input.Repository!.Trim()}'.");
            if (repository.Kind == RepositoryKind.Virtual) throw new RetentionException("A virtual repository stores nothing, so retention does not apply to it.");

            // A rule that can never apply to the one repository is a mistake worth refusing (a project mixes formats, so it is only noted in reports).
            if ((input.UntaggedAfterDays != null || input.CollectGarbage) && repository.Format != RepositoryFormat.Docker)
            {
                throw new RetentionException("Untagged manifests and the garbage collection apply to Docker repositories only.");
            }

            if (input.CacheLimitBytes != null && repository.Kind != RepositoryKind.Remote) throw new RetentionException("The cache limit applies to remote repositories only.");

            repositoryKey = repository.Key;
        }
        else
        {
            var project = await db.Projects!.AsNoTracking().FirstOrDefaultAsync(p => p.Name == input.Project!.Trim(), cancellationToken).ConfigureAwait(false)
                ?? throw new RetentionException($"There is no project '{input.Project!.Trim()}'.");
            projectKey = project.Key;
        }

        // Compared in memory, case-insensitively, whatever the engine's collation.
        var names = await db.RetentionPolicies!.AsNoTracking().Select(p => new { p.Key, p.Name }).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (names.Any(p => p.Key != input.Key && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) throw new RetentionException($"There is already a policy named '{name}'.");

        var now = _time.UtcNow;
        RetentionPolicy row;
        string action;
        if (input.Key == 0)
        {
            row = new RetentionPolicy { Created = now, UnusedSince = now };
            db.RetentionPolicies!.Add(row);
            action = "retention.policy.create";
        }
        else
        {
            row = await db.RetentionPolicies!.FirstOrDefaultAsync(p => p.Key == input.Key, cancellationToken).ConfigureAwait(false)
                ?? throw new RetentionException("The policy no longer exists.");
            action = "retention.policy.update";
        }

        // A changed schedule counts from now, so enabling a stale policy does not run it at once for the time it was off.
        var restartSchedule = input.Key == 0 || row.ScheduleHours != input.ScheduleHours || (!row.Enabled && input.Enabled);

        // The unused rule judges only time it watched: restart that clock when the rule is switched on or changed, or when the
        // scope it looks at (repository, project, name patterns) changes, since downloads of a newly covered version were not recorded.
        if (input.Key != 0 && (row.UnusedDays != input.UnusedDays || row.RepositoryKey != repositoryKey || row.ProjectKey != projectKey
            || !string.Equals(row.NamePatterns ?? string.Empty, patternText ?? string.Empty, StringComparison.Ordinal)))
        {
            row.UnusedSince = now;
        }

        row.Name = name;
        row.RepositoryKey = repositoryKey;
        row.ProjectKey = projectKey;
        row.Enabled = input.Enabled;
        row.NamePatterns = patternText;
        row.KeepLast = input.KeepLast;
        row.OlderThanDays = input.OlderThanDays;
        row.UnusedDays = input.UnusedDays;
        row.PrereleaseOnly = input.PrereleaseOnly;
        row.CacheLimitBytes = input.CacheLimitBytes;
        row.UntaggedAfterDays = input.UntaggedAfterDays;
        row.CollectGarbage = input.CollectGarbage;
        row.ScheduleHours = input.ScheduleHours;
        row.Modified = now;
        if (restartSchedule && row.ScheduleHours != null) row.ScheduleStart = now;

        AccessAdministration.Audit(db, now, actor, action, name, new
        {
            repository = input.Repository?.Trim(),
            project = input.Project?.Trim(),
            input.Enabled,
            input.KeepLast,
            input.OlderThanDays,
            input.UnusedDays,
            input.PrereleaseOnly,
            input.CacheLimitBytes,
            input.UntaggedAfterDays,
            input.CollectGarbage,
            input.ScheduleHours,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return row;
    }

    private static void Check(int? value, int min, int max, string field)
    {
        if (value is int v && (v < min || v > max)) throw new RetentionException($"{field} must be between {min} and {max}.");
    }

    /// <inheritdoc/>
    public async Task<bool> DeletePolicyAsync(int key, string? actor, CancellationToken cancellationToken)
    {
        var db = Db;
        _gate.ThrowIfClosed();

        var row = await db.RetentionPolicies!.FirstOrDefaultAsync(p => p.Key == key, cancellationToken).ConfigureAwait(false);
        if (row == null) return false;

        db.RetentionPolicies!.Remove(row);
        AccessAdministration.Audit(db, _time.UtcNow, actor, "retention.policy.delete", row.Name);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RetentionHoldView>> ListHoldsAsync(string? repository, CancellationToken cancellationToken)
    {
        var db = Db;
        var query = from h in db.RetentionHolds!.IgnoreQueryFilters().AsNoTracking()
                    join r in db.Repositories!.AsNoTracking() on h.RepositoryKey equals r.Key
                    select new { h, r.Name };
        if (!string.IsNullOrWhiteSpace(repository))
        {
            var name = repository.Trim();
            query = query.Where(x => x.Name == name);
        }

        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.h.Name).ThenBy(x => x.h.Version).ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows.Select(x => new RetentionHoldView(x.h.Key, x.h.RepositoryKey, x.Name!, x.h.Name!, x.h.Version ?? string.Empty, x.h.Reason, x.h.CreatedBy, x.h.Created)).ToList();
    }

    /// <inheritdoc/>
    public async Task<RetentionHold> AddHoldAsync(string repository, string name, string? version, string? reason, string? actor, CancellationToken cancellationToken)
    {
        var db = Db;
        _gate.ThrowIfClosed();

        var row = await db.Repositories!.AsNoTracking().FirstOrDefaultAsync(r => r.Name == (repository ?? string.Empty).Trim(), cancellationToken).ConfigureAwait(false)
            ?? throw new RetentionException($"There is no repository '{repository}'.");
        if (row.Kind == RepositoryKind.Virtual) throw new RetentionException("A virtual repository stores nothing, so there is nothing to hold.");

        var hooks = _services.GetFormatService<IFormatRetentionHooks>(row.Format)
            ?? throw new RetentionException($"The {row.Format} format has no retention support.");
        var canonical = hooks.Canonical(name ?? string.Empty, version)
            ?? throw new RetentionException($"'{name}' {(string.IsNullOrWhiteSpace(version) ? string.Empty : version)} is not a valid {row.Format} name or version.");
        if (canonical.Name.Length > RetentionLimits.MaxNameLength) throw new RetentionException("The name is too long.");
        if (canonical.Version.Length > RetentionLimits.MaxVersionLength) throw new RetentionException("The version is too long.");

        var reasonText = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (reasonText != null && reasonText.Length > RetentionLimits.MaxReasonLength) throw new RetentionException("The reason is too long.");

        // Names compare case-insensitively whatever the engine's collation, so look for a twin in memory.
        var sameName = await db.RetentionHolds!.IgnoreQueryFilters().Where(h => h.RepositoryKey == row.Key).ToListAsync(cancellationToken).ConfigureAwait(false);
        var twin = sameName.FirstOrDefault(h => string.Equals(h.Name, canonical.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(h.Version, canonical.Version, StringComparison.OrdinalIgnoreCase));
        if (twin != null) return twin;

        var now = _time.UtcNow;
        var hold = new RetentionHold { RepositoryKey = row.Key, Name = canonical.Name, Version = canonical.Version, Reason = reasonText, CreatedBy = actor, Created = now };
        db.RetentionHolds!.Add(hold);
        AccessAdministration.Audit(db, now, actor, "retention.hold.add", row.Name, new { name = canonical.Name, version = canonical.Version, reason = reasonText });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return hold;
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveHoldAsync(int holdKey, string? actor, CancellationToken cancellationToken)
    {
        var db = Db;
        _gate.ThrowIfClosed();

        var hold = await db.RetentionHolds!.IgnoreQueryFilters().FirstOrDefaultAsync(h => h.Key == holdKey, cancellationToken).ConfigureAwait(false);
        if (hold == null) return false;

        var repository = await db.Repositories!.AsNoTracking().Where(r => r.Key == hold.RepositoryKey).Select(r => r.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        db.RetentionHolds!.Remove(hold);
        AccessAdministration.Audit(db, _time.UtcNow, actor, "retention.hold.remove", repository, new { name = hold.Name, version = hold.Version });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }
}
