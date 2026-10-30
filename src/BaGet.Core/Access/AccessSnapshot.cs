using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core;

/// <summary>
/// Case-insensitive glob patterns in which <c>*</c> stands for any run of characters (package IDs and repository
/// names are case-insensitive). Compiled once into the <see cref="AccessSnapshot"/>, so a check allocates nothing.
/// </summary>
internal sealed class GlobSet
{
    private readonly string[] _patterns;
    private readonly bool _all;

    private GlobSet(string[] patterns)
    {
        _patterns = patterns;
        _all = patterns.Any(p => p == "*");
    }

    /// <param name="patterns">The globs; blank entries are ignored.</param>
    /// <param name="emptyMeansAll">Whether no pattern at all matches everything (include lists) or nothing.</param>
    public static GlobSet Parse(IEnumerable<string>? patterns, bool emptyMeansAll)
    {
        var list = (patterns ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToArray();
        return list.Length == 0 && emptyMeansAll ? new GlobSet(new[] { "*" }) : new GlobSet(list);
    }

    public bool IsMatch(string? value)
    {
        if (value == null) return false;
        if (_all) return true;

        foreach (var pattern in _patterns)
        {
            if (Match(pattern, value)) return true;
        }

        return false;
    }

    // The classic iterative wildcard match: remember the last '*' and retry from there on a mismatch.
    internal static bool Match(string pattern, string text)
    {
        int p = 0, t = 0, star = -1, mark = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = t;
            }
            else if (p < pattern.Length && char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(text[t]))
            {
                p++;
                t++;
            }
            else if (star >= 0)
            {
                p = star + 1;
                t = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }
}

/// <summary>The groups and organizations one user belongs to, besides the implicit built-in groups.</summary>
internal sealed record Membership(HashSet<int> GroupKeys, HashSet<int> OrganizationKeys)
{
    public static readonly Membership None = new(new HashSet<int>(), new HashSet<int>());
}

internal sealed record RepositoryRow(int Key, string Name, bool AnonymousRead, int? ProjectKey);

internal sealed record GrantRow(PrincipalType PrincipalType, string PrincipalId, AccessAction Actions);

internal sealed record TargetRow(int Key, string Name, string? Scope, GlobSet Repositories, GlobSet Include, GlobSet Exclude, int? ProjectKey, GrantRow[] Grants);

internal sealed record ProjectMemberRow(PrincipalType PrincipalType, string PrincipalId, ProjectRole Role);

/// <summary>
/// The whole access model, read from the database once and kept in memory until the model changes
/// (<see cref="AccessModelVersion"/>) or <see cref="AccessModelCache.TimeToLive"/> passes. Small by design: a few
/// hundred targets at most. What a user belongs to is loaded on first use and kept with the snapshot.
/// </summary>
internal sealed class AccessSnapshot
{
    private readonly ConcurrentDictionary<string, Membership> _memberships = new(StringComparer.Ordinal);

    public AccessSnapshot(
        long version,
        DateTime loaded,
        IReadOnlyDictionary<int, RepositoryRow> repositories,
        int? everyoneKey,
        int? authenticatedKey,
        IReadOnlyList<TargetRow> targets,
        IReadOnlyDictionary<int, ProjectMemberRow[]> projectMembers,
        IReadOnlyDictionary<int, string> groupNames,
        IReadOnlyDictionary<int, string> organizationNames,
        IReadOnlyDictionary<int, string> projectNames)
    {
        Version = version;
        Loaded = loaded;
        Repositories = repositories;
        EveryoneKey = everyoneKey;
        AuthenticatedKey = authenticatedKey;
        Targets = targets;
        ProjectMembers = projectMembers;
        GroupNames = groupNames;
        OrganizationNames = organizationNames;
        ProjectNames = projectNames;
    }

    public IReadOnlyDictionary<int, string> GroupNames { get; }

    public IReadOnlyDictionary<int, string> OrganizationNames { get; }

    public IReadOnlyDictionary<int, string> ProjectNames { get; }

    public long Version { get; }

    public DateTime Loaded { get; }

    public IReadOnlyDictionary<int, RepositoryRow> Repositories { get; }

    public int? EveryoneKey { get; }

    public int? AuthenticatedKey { get; }

    public IReadOnlyList<TargetRow> Targets { get; }

    public IReadOnlyDictionary<int, ProjectMemberRow[]> ProjectMembers { get; }

    public async Task<Membership> MembershipAsync(IContext context, string userId, CancellationToken cancellationToken)
    {
        if (_memberships.TryGetValue(userId, out var cached)) return cached;

        var groups = await context.GroupMembers!.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.GroupKey)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var organizations = await context.OrganizationMembers!.AsNoTracking()
            .Where(m => m.UserId == userId && m.OrganizationKey != null)
            .Select(m => m.OrganizationKey!.Value)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var membership = new Membership(groups.ToHashSet(), organizations.ToHashSet());
        _memberships[userId] = membership;
        return membership;
    }

    /// <summary>
    /// The actions <paramref name="principal"/> holds on <paramref name="repository"/>, and on <paramref name="packageId"/>
    /// when given. Targets with a package filter apply to a repository-level question (null ID) whatever the filter says,
    /// since some package in the repository may match.
    /// </summary>
    /// <param name="principal">Who asks; null is anonymous.</param>
    /// <param name="membership">The caller's groups and organizations.</param>
    /// <param name="repository">The repository.</param>
    /// <param name="packageId">The package ID, or null for the repository as a whole.</param>
    /// <param name="reasons">When given, receives one entry per grant or project role that applies: the explanation.</param>
    public AccessAction Granted(
        BaGetPrincipal? principal,
        Membership membership,
        RepositoryInfo repository,
        string? packageId,
        List<AccessReason>? reasons = null)
    {
        Repositories.TryGetValue(repository.Key, out var row);
        var anonymousRead = row?.AnonymousRead ?? true;
        var projectKey = row?.ProjectKey;
        var userId = principal?.UserId;

        var granted = AccessAction.None;

        foreach (var target in Targets)
        {
            if (!target.Repositories.IsMatch(repository.Name)) continue;
            if (target.ProjectKey != null && target.ProjectKey != projectKey) continue;
            if (packageId != null && (!target.Include.IsMatch(packageId) || target.Exclude.IsMatch(packageId))) continue;

            foreach (var grant in target.Grants)
            {
                if (!Applies(grant.PrincipalType, grant.PrincipalId, principal, userId, membership, out var viaEveryone)) continue;

                // Anonymous read is a property of the repository: while it is off, the Everyone group's Read does
                // not apply there (a user with a Read grant of their own still reads).
                var actions = viaEveryone && !anonymousRead ? grant.Actions & ~AccessAction.Read : grant.Actions;
                granted |= actions;
                reasons?.Add(Reason(target, grant, actions, viaEveryone && !anonymousRead, principal));
            }
        }

        if (projectKey != null && ProjectMembers.TryGetValue(projectKey.Value, out var members))
        {
            foreach (var member in members)
            {
                if (Applies(member.PrincipalType, member.PrincipalId, principal, userId, membership, out var viaEveryone))
                {
                    // Same rule as for a permission target: while anonymous read is off, Everyone's Read does not apply,
                    // whether it comes from a grant or from a project role.
                    var blocked = viaEveryone && !anonymousRead;
                    var actions = ProjectRoles.ActionsOf(member.Role);
                    if (blocked) actions &= ~AccessAction.Read;
                    granted |= actions;
                    reasons?.Add(ProjectReason(projectKey.Value, member, actions, blocked, principal));
                }
            }
        }

        return granted;
    }

    /// <summary>
    /// Every grant and project role that applies to <paramref name="repository"/> whoever holds it. Target package
    /// filters are reported in <see cref="AccessReason.Packages"/>, not applied.
    /// </summary>
    public IReadOnlyList<AccessReason> Holders(RepositoryInfo repository)
    {
        Repositories.TryGetValue(repository.Key, out var row);
        var anonymousRead = row?.AnonymousRead ?? true;
        var projectKey = row?.ProjectKey;
        var result = new List<AccessReason>();

        foreach (var target in Targets)
        {
            if (!target.Repositories.IsMatch(repository.Name)) continue;
            if (target.ProjectKey != null && target.ProjectKey != projectKey) continue;

            foreach (var grant in target.Grants)
            {
                var viaEveryone = grant.PrincipalType == PrincipalType.Group
                    && int.TryParse(grant.PrincipalId, out var group) && group == EveryoneKey;
                var blocked = viaEveryone && !anonymousRead;
                result.Add(Reason(target, grant, blocked ? grant.Actions & ~AccessAction.Read : grant.Actions, blocked, principal: null));
            }
        }

        if (projectKey != null && ProjectMembers.TryGetValue(projectKey.Value, out var members))
        {
            foreach (var member in members)
            {
                var blocked = member.PrincipalType == PrincipalType.Group
                    && int.TryParse(member.PrincipalId, out var group) && group == EveryoneKey && !anonymousRead;
                var actions = ProjectRoles.ActionsOf(member.Role);
                result.Add(ProjectReason(projectKey.Value, member, blocked ? actions & ~AccessAction.Read : actions, blocked, principal: null));
            }
        }

        return result;
    }

    private AccessReason Reason(TargetRow target, GrantRow grant, AccessAction effective, bool anonymousReadOff, BaGetPrincipal? principal)
        => new(
            $"Permission target '{target.Name}'",
            grant.PrincipalType,
            grant.PrincipalId,
            PrincipalText(grant.PrincipalType, grant.PrincipalId, principal),
            grant.Actions,
            effective,
            target.Scope,
            anonymousReadOff ? "Anonymous read is off for this repository, so Everyone's Read does not apply." : null);

    private AccessReason ProjectReason(int projectKey, ProjectMemberRow member, AccessAction effective, bool anonymousReadOff, BaGetPrincipal? principal)
    {
        var actions = ProjectRoles.ActionsOf(member.Role);
        var project = ProjectNames.TryGetValue(projectKey, out var name) ? name : projectKey.ToString();

        return new AccessReason(
            $"Project '{project}', role {member.Role}",
            member.PrincipalType,
            member.PrincipalId,
            PrincipalText(member.PrincipalType, member.PrincipalId, principal),
            actions,
            effective,
            Packages: null,
            Note: anonymousReadOff ? "Anonymous read is off for this repository, so Everyone's Read does not apply." : null);
    }

    private string PrincipalText(PrincipalType type, string id, BaGetPrincipal? principal)
    {
        var parsed = int.TryParse(id, out var key);

        return type switch
        {
            PrincipalType.User => "user " + (principal?.UserId == id ? (principal.UserName ?? id) : id),
            PrincipalType.Group => "group " + (parsed && GroupNames.TryGetValue(key, out var group) ? group : id),
            _ => "organization " + (parsed && OrganizationNames.TryGetValue(key, out var organization) ? organization : id),
        };
    }

    private bool Applies(
        PrincipalType type,
        string id,
        BaGetPrincipal? principal,
        string? userId,
        Membership membership,
        out bool viaEveryone)
    {
        viaEveryone = false;
        switch (type)
        {
            case PrincipalType.User:
                return userId != null && string.Equals(id, userId, StringComparison.Ordinal);

            case PrincipalType.Organization:
                return int.TryParse(id, out var organization) && membership.OrganizationKeys.Contains(organization);

            case PrincipalType.Group:
                if (!int.TryParse(id, out var group)) return false;
                if (group == EveryoneKey)
                {
                    viaEveryone = true;
                    return true;
                }

                if (group == AuthenticatedKey) return principal != null;
                return membership.GroupKeys.Contains(group);

            default:
                return false;
        }
    }
}

/// <summary>
/// A counter bumped after every committed change to the access model (see <see cref="AccessModelSaveChangesInterceptor"/>):
/// a snapshot older than the counter is reloaded. Per process; other instances catch up within
/// <see cref="AccessModelCache.TimeToLive"/>.
/// </summary>
public sealed class AccessModelVersion
{
    private long _current;

    public long Current => Interlocked.Read(ref _current);

    /// <summary>Called after <see cref="Bump"/> so a cluster can tell the other nodes (they reload their snapshot).</summary>
    internal Action? Publisher { get; set; }

    public void Bump()
    {
        Interlocked.Increment(ref _current);
        Publisher?.Invoke();
    }

    /// <summary>A change another node made: reload here, without telling the cluster again.</summary>
    internal void BumpFromCluster() => Interlocked.Increment(ref _current);
}

/// <summary>The singleton that owns the current <see cref="AccessSnapshot"/>.</summary>
public sealed class AccessModelCache
{
    /// <summary>
    /// How long a snapshot is trusted without a version bump, which bounds how stale another instance of the server
    /// can leave this one.
    /// </summary>
    public static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(30);

    private readonly AccessModelVersion _version;
    private volatile AccessSnapshot? _snapshot;

    public AccessModelCache(AccessModelVersion version)
    {
        _version = version ?? throw new ArgumentNullException(nameof(version));
    }

    internal async Task<AccessSnapshot> GetAsync(IContext context, CancellationToken cancellationToken)
    {
        var current = _snapshot;
        var now = DateTime.UtcNow;
        if (current != null && current.Version == _version.Current && now - current.Loaded < TimeToLive) return current;

        // Read the version first: a change committed while loading bumps it, and the snapshot is then already stale.
        var version = _version.Current;

        var repositories = (await context.Repositories!.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(r => r.Key, r => new RepositoryRow(r.Key, r.Name ?? string.Empty, r.AnonymousRead, r.ProjectKey));

        var groups = await context.Groups!.AsNoTracking().Where(g => g.IsBuiltIn).ToListAsync(cancellationToken).ConfigureAwait(false);
        var everyone = groups.FirstOrDefault(g => g.Name == BuiltInNames.Everyone)?.Key;
        var authenticated = groups.FirstOrDefault(g => g.Name == BuiltInNames.Authenticated)?.Key;

        var targets = await context.PermissionTargets!.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var grants = (await context.PermissionGrants!.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false))
            .GroupBy(g => g.TargetKey)
            .ToDictionary(g => g.Key, g => g.Select(x => new GrantRow(x.PrincipalType, x.PrincipalId ?? string.Empty, x.Actions)).ToArray());

        var rows = targets.Select(t => new TargetRow(
            t.Key,
            t.Name ?? string.Empty,
            AccessPatterns.DescribeScope(t.IncludePatterns, t.ExcludePatterns),
            GlobSet.Parse(t.RepositoryPatterns, emptyMeansAll: false),
            GlobSet.Parse(t.IncludePatterns, emptyMeansAll: true),
            GlobSet.Parse(t.ExcludePatterns, emptyMeansAll: false),
            t.ProjectKey,
            grants.TryGetValue(t.Key, out var g) ? g : Array.Empty<GrantRow>())).ToList();

        var members = (await context.ProjectMembers!.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false))
            .GroupBy(m => m.ProjectKey)
            .ToDictionary(g => g.Key, g => g.Select(m => new ProjectMemberRow(m.PrincipalType, m.PrincipalId ?? string.Empty, m.Role)).ToArray());

        var groupNames = (await context.Groups!.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(g => g.Key, g => g.Name ?? string.Empty);
        var organizationNames = (await context.Organizations!.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(o => o.Key, o => o.Name ?? string.Empty);
        var projectNames = (await context.Projects!.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(p => p.Key, p => p.Name ?? string.Empty);

        var loaded = new AccessSnapshot(version, now, repositories, everyone, authenticated, rows, members, groupNames, organizationNames, projectNames);
        _snapshot = loaded;
        return loaded;
    }
}
