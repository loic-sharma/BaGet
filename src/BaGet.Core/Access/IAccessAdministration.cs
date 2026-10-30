using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core;

/// <summary>A change to the access model that cannot be made, with a message fit for an administrator.</summary>
public class AccessModelException : Exception
{
    public AccessModelException(string message) : base(message) { }
}

/// <summary>
/// Edits the access model: groups and their members, permission targets and grants, project members. Every change is
/// written to <see cref="AccessAuditEntry"/> in the same save, and goes through the database context, so it is refused
/// while the <see cref="MaintenanceGate"/> is closed and invalidates the evaluator's cache
/// (<see cref="AccessModelSaveChangesInterceptor"/>). What the admin pages use.
/// </summary>
public interface IAccessAdministration
{
    /// <summary>Creates a group.</summary>
    /// <exception cref="AccessModelException">The name is invalid or taken.</exception>
    Task<Group> CreateGroupAsync(string name, string? description, string? actor, CancellationToken cancellationToken);

    /// <summary>Deletes a group, its members and every grant naming it.</summary>
    /// <exception cref="AccessModelException">Unknown group, or a built-in one.</exception>
    Task DeleteGroupAsync(string name, string? actor, CancellationToken cancellationToken);

    /// <summary>Adds a user to a group (idempotent).</summary>
    /// <exception cref="AccessModelException">Unknown group, or a built-in one (membership is implicit).</exception>
    Task AddGroupMemberAsync(string group, string userId, string? actor, CancellationToken cancellationToken);

    /// <summary>Removes a user from a group (idempotent).</summary>
    Task RemoveGroupMemberAsync(string group, string userId, string? actor, CancellationToken cancellationToken);

    /// <summary>Creates a permission target without grants.</summary>
    /// <param name="name">Unique name.</param>
    /// <param name="repositoryPatterns">Repository names or <c>*</c> globs.</param>
    /// <param name="includePatterns">Package ID globs; null or empty means all.</param>
    /// <param name="excludePatterns">Package ID globs to leave out.</param>
    /// <param name="projectKey">The administering project, if any.</param>
    /// <param name="actor">The user making the change.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="AccessModelException">The name is invalid or taken, or no repository pattern is given.</exception>
    Task<PermissionTarget> CreateTargetAsync(
        string name,
        IReadOnlyCollection<string> repositoryPatterns,
        IReadOnlyCollection<string>? includePatterns,
        IReadOnlyCollection<string>? excludePatterns,
        int? projectKey,
        string? actor,
        CancellationToken cancellationToken);

    /// <summary>Replaces a target's patterns.</summary>
    Task UpdateTargetAsync(
        string name,
        IReadOnlyCollection<string> repositoryPatterns,
        IReadOnlyCollection<string>? includePatterns,
        IReadOnlyCollection<string>? excludePatterns,
        string? actor,
        CancellationToken cancellationToken);

    /// <summary>Deletes a target and its grants (built-in ones too; <see cref="RestoreBuiltInsAsync"/> brings them back).</summary>
    Task DeleteTargetAsync(string name, string? actor, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the actions a principal holds through a target, replacing any earlier grant to the same principal;
    /// <see cref="AccessAction.None"/> removes it.
    /// </summary>
    /// <param name="target">The target's name.</param>
    /// <param name="type">What kind of principal.</param>
    /// <param name="principalId">A user's id, or a group's or organization's key.</param>
    /// <param name="actions">The actions granted.</param>
    /// <param name="actor">The user making the change.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task SetGrantAsync(
        string target,
        PrincipalType type,
        string principalId,
        AccessAction actions,
        string? actor,
        CancellationToken cancellationToken);

    /// <summary>Creates a project (no repositories yet: set <see cref="Repository.ProjectKey"/>).</summary>
    Task<Project> CreateProjectAsync(string name, string? displayName, string? actor, CancellationToken cancellationToken);

    /// <summary>Gives a user or group a role in a project, replacing an earlier one.</summary>
    Task SetProjectMemberAsync(
        string project,
        PrincipalType type,
        string principalId,
        ProjectRole role,
        string? actor,
        CancellationToken cancellationToken);

    /// <summary>Removes a user or group from a project.</summary>
    Task RemoveProjectMemberAsync(
        string project,
        PrincipalType type,
        string principalId,
        string? actor,
        CancellationToken cancellationToken);

    /// <summary>Renames a group. Built-in groups keep their names (the evaluator finds them by name).</summary>
    /// <exception cref="AccessModelException">Unknown or built-in group, or the new name is invalid or taken.</exception>
    Task RenameGroupAsync(string name, string newName, string? actor, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a project: its members, its permission targets (with their grants) and its link to repositories, which
    /// stay and become project-less. Audited.
    /// </summary>
    /// <exception cref="AccessModelException">Unknown project.</exception>
    Task DeleteProjectAsync(string name, string? actor, CancellationToken cancellationToken);

    /// <summary>Puts a repository in a project, or takes it out of its project when <paramref name="project"/> is null.</summary>
    /// <exception cref="AccessModelException">Unknown repository or project.</exception>
    Task SetRepositoryProjectAsync(string repository, string? project, string? actor, CancellationToken cancellationToken);

    /// <summary>Every group with its member count, ordered by name (built-ins first).</summary>
    Task<IReadOnlyList<GroupSummary>> ListGroupsAsync(CancellationToken cancellationToken);

    /// <returns>The group with its members and the grants naming it, or null.</returns>
    Task<GroupDetails?> GetGroupAsync(string name, CancellationToken cancellationToken);

    /// <summary>The names of the groups <paramref name="userId"/> is a member of (the implicit built-ins excluded).</summary>
    Task<IReadOnlyList<string>> GroupsOfUserAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Every permission target, ordered by name.</summary>
    Task<IReadOnlyList<TargetSummary>> ListTargetsAsync(CancellationToken cancellationToken);

    /// <returns>The target with its grants, or null.</returns>
    Task<TargetDetails?> GetTargetAsync(string name, CancellationToken cancellationToken);

    /// <summary>Every project with its counts, ordered by name.</summary>
    Task<IReadOnlyList<ProjectSummary>> ListProjectsAsync(CancellationToken cancellationToken);

    /// <returns>The project with its repositories, members and targets, or null.</returns>
    Task<ProjectDetails?> GetProjectAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// The roles <paramref name="userId"/> holds in projects, directly or through a group (including the built-in
    /// <c>Authenticated</c> and <c>Everyone</c> groups).
    /// </summary>
    Task<IReadOnlyList<UserProjectRole>> ProjectRolesOfUserAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Whether <paramref name="userId"/> holds the Admin role in <paramref name="project"/>.</summary>
    Task<bool> IsProjectAdminAsync(string userId, string project, CancellationToken cancellationToken);

    /// <summary>
    /// The id of the user, group or organization called <paramref name="name"/> (what <see cref="SetGrantAsync"/> and
    /// <see cref="SetProjectMemberAsync"/> take), or null when there is none.
    /// </summary>
    Task<string?> ResolvePrincipalAsync(PrincipalType type, string name, CancellationToken cancellationToken);

    /// <summary>Users whose name contains <paramref name="text"/>, ordered by name; for the member pickers.</summary>
    Task<IReadOnlyList<GroupMemberInfo>> SearchUsersAsync(string text, int take, CancellationToken cancellationToken);

    /// <summary>The audit entries matching <paramref name="filter"/>, newest first.</summary>
    Task<IReadOnlyList<AccessAuditEntry>> AuditAsync(AuditFilter filter, int take, CancellationToken cancellationToken);

    /// <summary>Re-creates the built-in groups and targets with their upgrade defaults where they are missing.</summary>
    Task RestoreBuiltInsAsync(string? actor, CancellationToken cancellationToken);

    /// <summary>The most recent audit entries, newest first.</summary>
    Task<IReadOnlyList<AccessAuditEntry>> AuditAsync(int take, CancellationToken cancellationToken);
}

/// <summary><see cref="IAccessAdministration"/> over the relational database.</summary>
public class AccessAdministration : IAccessAdministration
{
    private readonly IContext _context;
    private readonly SystemTime _time;

    public AccessAdministration(IContext context, SystemTime time)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// Adds an audit entry to the context without saving, so the caller's own save writes both.
    /// </summary>
    internal static void Audit(IContext context, DateTime now, string? actor, string action, string? subject, object? details = null)
    {
        context.AccessAuditEntries!.Add(new AccessAuditEntry
        {
            Created = now,
            Actor = actor,
            Action = action,
            Subject = subject,
            Details = details == null ? null : JsonSerializer.Serialize(details),
        });
    }

    /// <summary>
    /// Stages (no save) the removal of everything the access model holds for a principal that is going away: its
    /// group memberships (users only), permission grants and project roles. The caller saves it together with the
    /// deletion of the principal itself, so the database applies all of it or none of it; the interceptor then
    /// invalidates the evaluator's cache. Shared by the user and the organization deletion paths.
    /// </summary>
    /// <returns>How many rows were staged for removal.</returns>
    internal static async Task<int> StageCleanupAsync(IContext context, PrincipalType type, string? id, CancellationToken cancellationToken)
    {
        if (id == null || context.PermissionGrants == null) return 0;

        var removed = 0;
        if (type == PrincipalType.User)
        {
            var memberships = await context.GroupMembers!.Where(m => m.UserId == id).ToListAsync(cancellationToken).ConfigureAwait(false);
            context.GroupMembers!.RemoveRange(memberships);
            removed += memberships.Count;
        }

        var grants = await context.PermissionGrants.Where(g => g.PrincipalType == type && g.PrincipalId == id).ToListAsync(cancellationToken).ConfigureAwait(false);
        context.PermissionGrants.RemoveRange(grants);
        var roles = await context.ProjectMembers!.Where(m => m.PrincipalType == type && m.PrincipalId == id).ToListAsync(cancellationToken).ConfigureAwait(false);
        context.ProjectMembers!.RemoveRange(roles);

        return removed + grants.Count + roles.Count;
    }

    private void Audit(string? actor, string action, string? subject, object? details = null)
        => Audit(_context, _time.UtcNow, actor, action, subject, details);

    /// <inheritdoc/>
    public async Task<Group> CreateGroupAsync(string name, string? description, string? actor, CancellationToken cancellationToken)
    {
        if (!AccessNames.IsValid(name, out var error)) throw new AccessModelException(error!);
        if (await FindGroupAsync(name, cancellationToken).ConfigureAwait(false) != null)
        {
            throw new AccessModelException($"There is already a group named '{name}'.");
        }

        var group = new Group { Name = name, Description = description, Created = _time.UtcNow };
        _context.Groups!.Add(group);
        Audit(actor, "group.create", name);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return group;
    }

    /// <inheritdoc/>
    public async Task DeleteGroupAsync(string name, string? actor, CancellationToken cancellationToken)
    {
        var group = await GroupAsync(name, cancellationToken).ConfigureAwait(false);
        if (group.IsBuiltIn) throw new AccessModelException($"'{group.Name}' is a built-in group and cannot be deleted.");

        var id = group.Key.ToString();
        var grants = await _context.PermissionGrants!
            .Where(g => g.PrincipalType == PrincipalType.Group && g.PrincipalId == id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var projectRoles = await _context.ProjectMembers!
            .Where(m => m.PrincipalType == PrincipalType.Group && m.PrincipalId == id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        _context.PermissionGrants!.RemoveRange(grants);
        _context.ProjectMembers!.RemoveRange(projectRoles);
        _context.Groups!.Remove(group);
        Audit(actor, "group.delete", group.Name, new { grants = grants.Count, projectMemberships = projectRoles.Count });
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task AddGroupMemberAsync(string group, string userId, string? actor, CancellationToken cancellationToken)
    {
        var row = await GroupAsync(group, cancellationToken).ConfigureAwait(false);
        if (row.IsBuiltIn) throw new AccessModelException($"Membership of the built-in group '{row.Name}' is implicit.");
        if (string.IsNullOrEmpty(userId)) throw new AccessModelException("A user is required.");
        // GroupMember.UserId is a foreign key to the users table: say so instead of failing the save.
        if (!await _context.Users.AnyAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false)) throw new AccessModelException("There is no such user.");

        if (await _context.GroupMembers!.AnyAsync(m => m.GroupKey == row.Key && m.UserId == userId, cancellationToken).ConfigureAwait(false)) return;

        _context.GroupMembers!.Add(new GroupMember { GroupKey = row.Key, UserId = userId });
        Audit(actor, "group.member.add", row.Name, new { userId });
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task RemoveGroupMemberAsync(string group, string userId, string? actor, CancellationToken cancellationToken)
    {
        var row = await GroupAsync(group, cancellationToken).ConfigureAwait(false);
        var member = await _context.GroupMembers!.FirstOrDefaultAsync(m => m.GroupKey == row.Key && m.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (member == null) return;

        _context.GroupMembers!.Remove(member);
        Audit(actor, "group.member.remove", row.Name, new { userId });
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<PermissionTarget> CreateTargetAsync(
        string name,
        IReadOnlyCollection<string> repositoryPatterns,
        IReadOnlyCollection<string>? includePatterns,
        IReadOnlyCollection<string>? excludePatterns,
        int? projectKey,
        string? actor,
        CancellationToken cancellationToken)
    {
        if (!AccessNames.IsValid(name, out var error)) throw new AccessModelException(error!);
        if (await FindTargetAsync(name, cancellationToken).ConfigureAwait(false) != null)
        {
            throw new AccessModelException($"There is already a permission target named '{name}'.");
        }

        var target = new PermissionTarget { Name = name, ProjectKey = projectKey, Created = _time.UtcNow };
        Apply(target, repositoryPatterns, includePatterns, excludePatterns);
        _context.PermissionTargets!.Add(target);
        Audit(actor, "target.create", name, Describe(target));
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return target;
    }

    /// <inheritdoc/>
    public async Task UpdateTargetAsync(
        string name,
        IReadOnlyCollection<string> repositoryPatterns,
        IReadOnlyCollection<string>? includePatterns,
        IReadOnlyCollection<string>? excludePatterns,
        string? actor,
        CancellationToken cancellationToken)
    {
        var target = await TargetAsync(name, cancellationToken).ConfigureAwait(false);
        Apply(target, repositoryPatterns, includePatterns, excludePatterns);
        Audit(actor, "target.update", name, Describe(target));
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteTargetAsync(string name, string? actor, CancellationToken cancellationToken)
    {
        var target = await TargetAsync(name, cancellationToken).ConfigureAwait(false);
        var grants = await _context.PermissionGrants!.Where(g => g.TargetKey == target.Key).ToListAsync(cancellationToken).ConfigureAwait(false);

        _context.PermissionGrants!.RemoveRange(grants);
        _context.PermissionTargets!.Remove(target);
        Audit(actor, "target.delete", name, new { grants = grants.Count });
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SetGrantAsync(
        string target,
        PrincipalType type,
        string principalId,
        AccessAction actions,
        string? actor,
        CancellationToken cancellationToken)
    {
        var row = await TargetAsync(target, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(principalId)) throw new AccessModelException("A principal is required.");
        if ((actions & ~AccessAction.All) != 0) throw new AccessModelException("Unknown actions.");

        var grant = await _context.PermissionGrants!.FirstOrDefaultAsync(
            g => g.TargetKey == row.Key && g.PrincipalType == type && g.PrincipalId == principalId, cancellationToken).ConfigureAwait(false);

        if (actions == AccessAction.None)
        {
            if (grant == null) return;
            _context.PermissionGrants!.Remove(grant);
        }
        else if (grant == null)
        {
            _context.PermissionGrants!.Add(new PermissionGrant { TargetKey = row.Key, PrincipalType = type, PrincipalId = principalId, Actions = actions });
        }
        else
        {
            grant.Actions = actions;
        }

        Audit(actor, "grant.set", row.Name, new { principalType = type.ToString(), principalId, actions = actions.ToString() });
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Project> CreateProjectAsync(string name, string? displayName, string? actor, CancellationToken cancellationToken)
    {
        if (!AccessNames.IsValid(name, out var error)) throw new AccessModelException(error!);
        if (await _context.Projects!.AnyAsync(p => p.Name == name, cancellationToken).ConfigureAwait(false))
        {
            throw new AccessModelException($"There is already a project named '{name}'.");
        }

        var project = new Project { Name = name, DisplayName = displayName, Created = _time.UtcNow };
        _context.Projects!.Add(project);
        Audit(actor, "project.create", name);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return project;
    }

    /// <inheritdoc/>
    public async Task SetProjectMemberAsync(
        string project,
        PrincipalType type,
        string principalId,
        ProjectRole role,
        string? actor,
        CancellationToken cancellationToken)
    {
        if (type == PrincipalType.Organization) throw new AccessModelException("A project member is a user or a group.");
        var row = await ProjectAsync(project, cancellationToken).ConfigureAwait(false);

        var member = await _context.ProjectMembers!.FirstOrDefaultAsync(
            m => m.ProjectKey == row.Key && m.PrincipalType == type && m.PrincipalId == principalId, cancellationToken).ConfigureAwait(false);
        if (member == null)
        {
            _context.ProjectMembers!.Add(new ProjectMember { ProjectKey = row.Key, PrincipalType = type, PrincipalId = principalId, Role = role });
        }
        else
        {
            member.Role = role;
        }

        Audit(actor, "project.member.set", row.Name, new { principalType = type.ToString(), principalId, role = role.ToString() });
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task RemoveProjectMemberAsync(string project, PrincipalType type, string principalId, string? actor, CancellationToken cancellationToken)
    {
        var row = await ProjectAsync(project, cancellationToken).ConfigureAwait(false);
        var member = await _context.ProjectMembers!.FirstOrDefaultAsync(
            m => m.ProjectKey == row.Key && m.PrincipalType == type && m.PrincipalId == principalId, cancellationToken).ConfigureAwait(false);
        if (member == null) return;

        _context.ProjectMembers!.Remove(member);
        Audit(actor, "project.member.remove", row.Name, new { principalType = type.ToString(), principalId });
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task RenameGroupAsync(string name, string newName, string? actor, CancellationToken cancellationToken)
    {
        var group = await GroupAsync(name, cancellationToken).ConfigureAwait(false);
        if (group.IsBuiltIn) throw new AccessModelException($"'{group.Name}' is a built-in group and cannot be renamed.");
        if (!AccessNames.IsValid(newName, out var error)) throw new AccessModelException(error!);

        var other = await FindGroupAsync(newName, cancellationToken).ConfigureAwait(false);
        if (other != null && other.Key != group.Key) throw new AccessModelException($"There is already a group named '{newName}'.");

        var old = group.Name;
        group.Name = newName;
        Audit(actor, "group.rename", newName, new { from = old });
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteProjectAsync(string name, string? actor, CancellationToken cancellationToken)
    {
        var project = await ProjectAsync(name, cancellationToken).ConfigureAwait(false);

        var targets = await _context.PermissionTargets!.Where(t => t.ProjectKey == project.Key).ToListAsync(cancellationToken).ConfigureAwait(false);
        var targetKeys = targets.Select(t => t.Key).ToList();
        var grants = await _context.PermissionGrants!.Where(g => targetKeys.Contains(g.TargetKey)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var members = await _context.ProjectMembers!.Where(m => m.ProjectKey == project.Key).ToListAsync(cancellationToken).ConfigureAwait(false);
        var repositories = await _context.Repositories!.Where(r => r.ProjectKey == project.Key).ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var repository in repositories) repository.ProjectKey = null;
        var scanPolicies = await _context.ScanPolicies!.Where(p => p.ProjectKey == project.Key).ToListAsync(cancellationToken).ConfigureAwait(false);
        _context.ScanPolicies!.RemoveRange(scanPolicies);
        // The project's retention policy applies to nothing once it is gone (and its ProjectKey is a foreign key). A run of it that is
        // still going ends normally: it only reads the policy up front and RetentionEngine.RecordRunAsync skips a policy that is gone.
        var retentionPolicies = await _context.RetentionPolicies!.Where(p => p.ProjectKey == project.Key).ToListAsync(cancellationToken).ConfigureAwait(false);
        _context.RetentionPolicies!.RemoveRange(retentionPolicies);
        foreach (var policy in retentionPolicies) Audit(actor, "retention.policy.delete", policy.Name, new { project = project.Name, reason = "project deleted" });
        _context.PermissionGrants!.RemoveRange(grants);
        _context.PermissionTargets!.RemoveRange(targets);
        _context.ProjectMembers!.RemoveRange(members);
        _context.Projects!.Remove(project);
        Audit(actor, "project.delete", project.Name, new { targets = targets.Count, members = members.Count, repositories = repositories.Count, scanPolicies = scanPolicies.Count, retentionPolicies = retentionPolicies.Count });
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SetRepositoryProjectAsync(string repository, string? project, string? actor, CancellationToken cancellationToken)
    {
        var row = await _context.Repositories!.FirstOrDefaultAsync(r => r.Name == repository, cancellationToken).ConfigureAwait(false)
                  ?? throw new AccessModelException($"There is no repository named '{repository}'.");
        var target = project == null ? null : await ProjectAsync(project, cancellationToken).ConfigureAwait(false);

        row.ProjectKey = target?.Key;
        Audit(actor, "repository.project", repository, new { project });
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<GroupSummary>> ListGroupsAsync(CancellationToken cancellationToken)
    {
        var groups = await _context.Groups!.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var counts = await _context.GroupMembers!.AsNoTracking()
            .GroupBy(m => m.GroupKey)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken).ConfigureAwait(false);

        return groups
            .OrderBy(g => g.IsBuiltIn ? 0 : 1)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => Summarize(g, counts))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<GroupDetails?> GetGroupAsync(string name, CancellationToken cancellationToken)
    {
        var group = await FindGroupAsync(name, cancellationToken).ConfigureAwait(false);
        if (group == null) return null;
        var groupKey = group.Key;

        var members = await (from m in _context.GroupMembers!.AsNoTracking()
                             join u in _context.Users.AsNoTracking() on m.UserId equals u.Id
                             where m.GroupKey == groupKey
                             orderby u.UserName
                             select new GroupMemberInfo(u.Id, u.UserName ?? u.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);

        var id = group.Key.ToString();
        var grants = await _context.PermissionGrants!.AsNoTracking()
            .Where(g => g.PrincipalType == PrincipalType.Group && g.PrincipalId == id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new GroupDetails(
            new GroupSummary(group.Key, group.Name ?? string.Empty, group.Description, group.IsBuiltIn, members.Count),
            members,
            await DescribeGrantsAsync(grants, cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GroupsOfUserAsync(string userId, CancellationToken cancellationToken)
        => await (from m in _context.GroupMembers!.AsNoTracking()
                  join g in _context.Groups!.AsNoTracking() on m.GroupKey equals g.Key
                  where m.UserId == userId
                  orderby g.Name
                  select g.Name!).ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TargetSummary>> ListTargetsAsync(CancellationToken cancellationToken)
    {
        var targets = await _context.PermissionTargets!.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var counts = await _context.PermissionGrants!.AsNoTracking()
            .GroupBy(g => g.TargetKey)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken).ConfigureAwait(false);

        return await SummarizeAsync(targets, counts, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<TargetDetails?> GetTargetAsync(string name, CancellationToken cancellationToken)
    {
        var target = await FindTargetAsync(name, cancellationToken).ConfigureAwait(false);
        if (target == null) return null;

        var grants = await _context.PermissionGrants!.AsNoTracking().Where(g => g.TargetKey == target.Key).ToListAsync(cancellationToken).ConfigureAwait(false);
        var summary = (await SummarizeAsync(new[] { target }, new Dictionary<int, int> { { target.Key, grants.Count } }, cancellationToken).ConfigureAwait(false))[0];

        return new TargetDetails(summary, await DescribeGrantsAsync(grants, cancellationToken, target.Name).ConfigureAwait(false));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProjectSummary>> ListProjectsAsync(CancellationToken cancellationToken)
    {
        var projects = await _context.Projects!.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var repositories = await _context.Repositories!.AsNoTracking().Where(r => r.ProjectKey != null)
            .GroupBy(r => r.ProjectKey!.Value).Select(g => new { Key = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken).ConfigureAwait(false);
        var members = await _context.ProjectMembers!.AsNoTracking()
            .GroupBy(m => m.ProjectKey).Select(g => new { Key = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken).ConfigureAwait(false);

        return projects
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new ProjectSummary(
                p.Key,
                p.Name ?? string.Empty,
                p.DisplayName,
                repositories.TryGetValue(p.Key, out var r) ? r : 0,
                members.TryGetValue(p.Key, out var m) ? m : 0))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<ProjectDetails?> GetProjectAsync(string name, CancellationToken cancellationToken)
    {
        var project = await _context.Projects!.AsNoTracking().FirstOrDefaultAsync(p => p.Name == name, cancellationToken).ConfigureAwait(false);
        if (project == null) return null;

        var repositories = await _context.Repositories!.AsNoTracking()
            .Where(r => r.ProjectKey == project.Key).Select(r => r.Name!).ToListAsync(cancellationToken).ConfigureAwait(false);
        var members = await _context.ProjectMembers!.AsNoTracking().Where(m => m.ProjectKey == project.Key).ToListAsync(cancellationToken).ConfigureAwait(false);
        var targets = await _context.PermissionTargets!.AsNoTracking().Where(t => t.ProjectKey == project.Key).ToListAsync(cancellationToken).ConfigureAwait(false);
        var counts = await _context.PermissionGrants!.AsNoTracking()
            .GroupBy(g => g.TargetKey).Select(g => new { Key = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken).ConfigureAwait(false);

        var namer = await PrincipalNamerAsync(members.Select(m => (m.PrincipalType, m.PrincipalId ?? string.Empty)), cancellationToken).ConfigureAwait(false);

        return new ProjectDetails(
            new ProjectSummary(project.Key, project.Name ?? string.Empty, project.DisplayName, repositories.Count, members.Count),
            repositories.OrderBy(r => r, StringComparer.Ordinal).ToList(),
            members
                .Select(m => new ProjectMemberInfo(m.PrincipalType, m.PrincipalId ?? string.Empty, namer(m.PrincipalType, m.PrincipalId ?? string.Empty), m.Role))
                .OrderBy(m => m.Principal, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            await SummarizeAsync(targets, counts, cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<UserProjectRole>> ProjectRolesOfUserAsync(string userId, CancellationToken cancellationToken)
    {
        var groupKeys = await GroupKeysOfAsync(userId, cancellationToken).ConfigureAwait(false);
        var groupNames = await _context.Groups!.AsNoTracking().ToDictionaryAsync(g => g.Key, g => g.Name ?? string.Empty, cancellationToken).ConfigureAwait(false);
        var projects = await _context.Projects!.AsNoTracking().ToDictionaryAsync(p => p.Key, p => p.Name ?? string.Empty, cancellationToken).ConfigureAwait(false);
        var members = await _context.ProjectMembers!.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

        var roles = new List<UserProjectRole>();
        foreach (var member in members)
        {
            if (!projects.TryGetValue(member.ProjectKey, out var project)) continue;

            if (member.PrincipalType == PrincipalType.User && member.PrincipalId == userId)
            {
                roles.Add(new UserProjectRole(project, member.Role, "directly"));
            }
            else if (member.PrincipalType == PrincipalType.Group
                     && int.TryParse(member.PrincipalId, out var group) && groupKeys.Contains(group))
            {
                roles.Add(new UserProjectRole(project, member.Role, "through group " + (groupNames.TryGetValue(group, out var n) ? n : member.PrincipalId)));
            }
        }

        return roles.OrderBy(r => r.Project, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <inheritdoc/>
    public async Task<bool> IsProjectAdminAsync(string userId, string project, CancellationToken cancellationToken)
        => (await ProjectRolesOfUserAsync(userId, cancellationToken).ConfigureAwait(false))
            .Any(r => r.Role == ProjectRole.Admin && string.Equals(r.Project, project, StringComparison.Ordinal));

    /// <inheritdoc/>
    public async Task<string?> ResolvePrincipalAsync(PrincipalType type, string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim();
        var lower = name.ToLowerInvariant();

        switch (type)
        {
            case PrincipalType.User:
                var upper = name.ToUpperInvariant();
                return await _context.Users.AsNoTracking()
                    .Where(u => u.NormalizedUserName == upper)
                    .Select(u => u.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            case PrincipalType.Group:
                return (await FindGroupAsync(name, cancellationToken).ConfigureAwait(false))?.Key.ToString();

            default:
                var organization = await _context.Organizations!.AsNoTracking()
                    .FirstOrDefaultAsync(o => o.Name!.ToLower() == lower, cancellationToken).ConfigureAwait(false);
                return organization?.Key.ToString();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<GroupMemberInfo>> SearchUsersAsync(string text, int take, CancellationToken cancellationToken)
    {
        var upper = (text ?? string.Empty).Trim().ToUpperInvariant();

        return await _context.Users.AsNoTracking()
            .Where(u => u.NormalizedUserName!.Contains(upper))
            .OrderBy(u => u.UserName)
            .Take(Math.Max(1, take))
            .Select(u => new GroupMemberInfo(u.Id, u.UserName ?? u.Id))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AccessAuditEntry>> AuditAsync(AuditFilter filter, int take, CancellationToken cancellationToken)
    {
        IQueryable<AccessAuditEntry> query = _context.AccessAuditEntries!.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter?.Actor))
        {
            var actor = filter!.Actor!.Trim().ToLowerInvariant();
            query = query.Where(e => e.Actor!.ToLower() == actor);
        }

        if (!string.IsNullOrWhiteSpace(filter?.Subject))
        {
            var subject = filter!.Subject!.Trim().ToLowerInvariant();
            query = query.Where(e => e.Subject!.ToLower().Contains(subject));
        }

        if (filter?.From != null)
        {
            var from = filter.From.Value;
            query = query.Where(e => e.Created >= from);
        }

        if (filter?.To != null)
        {
            var to = filter.To.Value;
            query = query.Where(e => e.Created < to);
        }

        return await query
            .OrderByDescending(e => e.Created).ThenByDescending(e => e.Key)
            .Take(Math.Max(1, take))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private static GroupSummary Summarize(Group group, IReadOnlyDictionary<int, int> counts)
        => new(group.Key, group.Name ?? string.Empty, group.Description, group.IsBuiltIn, counts.TryGetValue(group.Key, out var n) ? n : 0);

    private async Task<IReadOnlyList<TargetSummary>> SummarizeAsync(
        IEnumerable<PermissionTarget> targets,
        IReadOnlyDictionary<int, int> grantCounts,
        CancellationToken cancellationToken)
    {
        var projects = await _context.Projects!.AsNoTracking().ToDictionaryAsync(p => p.Key, p => p.Name ?? string.Empty, cancellationToken).ConfigureAwait(false);

        return targets
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => new TargetSummary(
                t.Key,
                t.Name ?? string.Empty,
                t.RepositoryPatterns ?? Array.Empty<string>(),
                t.IncludePatterns ?? Array.Empty<string>(),
                t.ExcludePatterns ?? Array.Empty<string>(),
                t.ProjectKey,
                t.ProjectKey != null && projects.TryGetValue(t.ProjectKey.Value, out var project) ? project : null,
                t.IsBuiltIn,
                grantCounts.TryGetValue(t.Key, out var n) ? n : 0))
            .ToList();
    }

    private async Task<IReadOnlyList<GrantInfo>> DescribeGrantsAsync(
        IReadOnlyList<PermissionGrant> grants,
        CancellationToken cancellationToken,
        string? targetName = null)
    {
        var namer = await PrincipalNamerAsync(grants.Select(g => (g.PrincipalType, g.PrincipalId ?? string.Empty)), cancellationToken).ConfigureAwait(false);
        var targets = targetName != null
            ? null
            : await _context.PermissionTargets!.AsNoTracking().ToDictionaryAsync(t => t.Key, t => t.Name ?? string.Empty, cancellationToken).ConfigureAwait(false);

        return grants
            .Select(g => new GrantInfo(
                targetName ?? (targets!.TryGetValue(g.TargetKey, out var t) ? t : g.TargetKey.ToString()),
                g.PrincipalType,
                g.PrincipalId ?? string.Empty,
                namer(g.PrincipalType, g.PrincipalId ?? string.Empty),
                g.Actions))
            .OrderBy(g => g.Target, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Principal, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Resolves principals to words with one query per kind, for the ids that are asked about.
    private async Task<Func<PrincipalType, string, string>> PrincipalNamerAsync(
        IEnumerable<(PrincipalType Type, string Id)> principals,
        CancellationToken cancellationToken)
    {
        var list = principals.ToList();
        var groupKeys = list.Where(p => p.Type == PrincipalType.Group && int.TryParse(p.Id, out _)).Select(p => int.Parse(p.Id)).Distinct().ToList();
        var organizationKeys = list.Where(p => p.Type == PrincipalType.Organization && int.TryParse(p.Id, out _)).Select(p => int.Parse(p.Id)).Distinct().ToList();
        var userIds = list.Where(p => p.Type == PrincipalType.User).Select(p => p.Id).Distinct().ToList();

        var groups = await _context.Groups!.AsNoTracking().Where(g => groupKeys.Contains(g.Key)).ToDictionaryAsync(g => g.Key, g => g.Name ?? string.Empty, cancellationToken).ConfigureAwait(false);
        var organizations = await _context.Organizations!.AsNoTracking().Where(o => organizationKeys.Contains(o.Key)).ToDictionaryAsync(o => o.Key, o => o.Name ?? string.Empty, cancellationToken).ConfigureAwait(false);
        var users = await _context.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName ?? u.Id, cancellationToken).ConfigureAwait(false);

        return (type, id) => type switch
        {
            PrincipalType.User => "user " + (users.TryGetValue(id, out var user) ? user : id),
            PrincipalType.Group => "group " + (int.TryParse(id, out var g) && groups.TryGetValue(g, out var group) ? group : id),
            _ => "organization " + (int.TryParse(id, out var o) && organizations.TryGetValue(o, out var organization) ? organization : id),
        };
    }

    // The keys of the groups whose members include the user, built-ins that apply to every signed-in caller included.
    private async Task<HashSet<int>> GroupKeysOfAsync(string userId, CancellationToken cancellationToken)
    {
        var keys = await _context.GroupMembers!.AsNoTracking().Where(m => m.UserId == userId).Select(m => m.GroupKey).ToListAsync(cancellationToken).ConfigureAwait(false);
        var builtIn = await _context.Groups!.AsNoTracking().Where(g => g.IsBuiltIn).Select(g => g.Key).ToListAsync(cancellationToken).ConfigureAwait(false);

        return keys.Concat(builtIn).ToHashSet();
    }

    /// <inheritdoc/>
    public async Task RestoreBuiltInsAsync(string? actor, CancellationToken cancellationToken)
    {
        var everyone = await EnsureBuiltInGroupAsync(BuiltInNames.Everyone, "Every caller, anonymous ones included.", cancellationToken).ConfigureAwait(false);
        var authenticated = await EnsureBuiltInGroupAsync(BuiltInNames.Authenticated, "Every caller that presented valid credentials.", cancellationToken).ConfigureAwait(false);

        // Keys are generated here, so save the groups before naming them in grants.
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await EnsureBuiltInTargetAsync(BuiltInNames.AnyoneReads, everyone, AccessAction.Read, cancellationToken).ConfigureAwait(false);
        await EnsureBuiltInTargetAsync(
            BuiltInNames.AuthenticatedDeploy,
            authenticated,
            AccessAction.Annotate | AccessAction.Deploy | AccessAction.Delete | AccessAction.Manage,
            cancellationToken).ConfigureAwait(false);

        Audit(actor, "builtins.restore", null);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AccessAuditEntry>> AuditAsync(int take, CancellationToken cancellationToken)
        => await _context.AccessAuditEntries!.AsNoTracking()
            .OrderByDescending(e => e.Created).ThenByDescending(e => e.Key)
            .Take(Math.Max(1, take))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    private async Task<Group> EnsureBuiltInGroupAsync(string name, string description, CancellationToken cancellationToken)
    {
        var group = await FindGroupAsync(name, cancellationToken).ConfigureAwait(false);
        if (group != null)
        {
            group.IsBuiltIn = true;
            return group;
        }

        group = new Group { Name = name, Description = description, Created = _time.UtcNow, IsBuiltIn = true };
        _context.Groups!.Add(group);
        return group;
    }

    private async Task EnsureBuiltInTargetAsync(string name, Group group, AccessAction actions, CancellationToken cancellationToken)
    {
        var target = await FindTargetAsync(name, cancellationToken).ConfigureAwait(false);
        if (target == null)
        {
            target = new PermissionTarget
            {
                Name = name,
                RepositoryPatterns = new[] { "*" },
                IncludePatterns = new[] { "*" },
                ExcludePatterns = Array.Empty<string>(),
                IsBuiltIn = true,
                Created = _time.UtcNow,
            };
            _context.PermissionTargets!.Add(target);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var id = group.Key.ToString();
        if (!await _context.PermissionGrants!.AnyAsync(
                g => g.TargetKey == target.Key && g.PrincipalType == PrincipalType.Group && g.PrincipalId == id, cancellationToken).ConfigureAwait(false))
        {
            _context.PermissionGrants!.Add(new PermissionGrant { TargetKey = target.Key, PrincipalType = PrincipalType.Group, PrincipalId = id, Actions = actions });
        }
    }

    private static void Apply(
        PermissionTarget target,
        IReadOnlyCollection<string> repositoryPatterns,
        IReadOnlyCollection<string>? includePatterns,
        IReadOnlyCollection<string>? excludePatterns)
    {
        string[] Clean(IEnumerable<string>? patterns)
            => (patterns ?? Array.Empty<string>()).Select(p => p?.Trim() ?? string.Empty).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var repositories = Clean(repositoryPatterns);
        if (repositories.Length == 0) throw new AccessModelException("A permission target needs at least one repository pattern.");

        target.RepositoryPatterns = repositories;
        var include = Clean(includePatterns);
        target.IncludePatterns = include.Length == 0 ? new[] { "*" } : include;
        target.ExcludePatterns = Clean(excludePatterns);
    }

    private static object Describe(PermissionTarget target) => new
    {
        repositories = target.RepositoryPatterns,
        include = target.IncludePatterns,
        exclude = target.ExcludePatterns,
        projectKey = target.ProjectKey,
    };

    private Task<Group?> FindGroupAsync(string name, CancellationToken cancellationToken)
    {
        var lower = name.ToLowerInvariant();
        return _context.Groups!.FirstOrDefaultAsync(g => g.Name!.ToLower() == lower, cancellationToken);
    }

    private Task<PermissionTarget?> FindTargetAsync(string name, CancellationToken cancellationToken)
    {
        var lower = name.ToLowerInvariant();
        return _context.PermissionTargets!.FirstOrDefaultAsync(t => t.Name!.ToLower() == lower, cancellationToken);
    }

    private async Task<Group> GroupAsync(string name, CancellationToken cancellationToken)
        => await FindGroupAsync(name, cancellationToken).ConfigureAwait(false) ?? throw new AccessModelException($"There is no group named '{name}'.");

    private async Task<PermissionTarget> TargetAsync(string name, CancellationToken cancellationToken)
        => await FindTargetAsync(name, cancellationToken).ConfigureAwait(false) ?? throw new AccessModelException($"There is no permission target named '{name}'.");

    private async Task<Project> ProjectAsync(string name, CancellationToken cancellationToken)
        => await _context.Projects!.FirstOrDefaultAsync(p => p.Name == name, cancellationToken).ConfigureAwait(false)
           ?? throw new AccessModelException($"There is no project named '{name}'.");
}
