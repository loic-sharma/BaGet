using System;

namespace BaGet.Core;

/// <summary>
/// What a permission grants. Independent flags: <see cref="Deploy"/> does not imply <see cref="Read"/>, as in
/// Artifactory. Package ownership (<see cref="IPackageAuthorizationService"/>) is a second, separate requirement
/// for the actions that change an existing package ID.
/// </summary>
[Flags]
public enum AccessAction
{
    None = 0,

    /// <summary>Use the repository's feed, symbols and pages.</summary>
    Read = 1,

    /// <summary>Edit a version's deprecation and vulnerabilities.</summary>
    Annotate = 2,

    /// <summary>Push new packages and new versions.</summary>
    Deploy = 4,

    /// <summary>Unlist, relist and delete versions.</summary>
    Delete = 8,

    /// <summary>Administer the owners of a package ID and the repository's targets.</summary>
    Manage = 16,

    /// <summary>Every action.</summary>
    All = Read | Annotate | Deploy | Delete | Manage,
}

/// <summary>Who a <see cref="PermissionGrant"/> or <see cref="ProjectMember"/> names.</summary>
public enum PrincipalType
{
    /// <summary>A user; the principal id is <c>BaGetUser.Id</c>.</summary>
    User,

    /// <summary>A <see cref="Group"/>; the principal id is its <see cref="Group.Key"/>.</summary>
    Group,

    /// <summary>An <see cref="Organization"/> (its members); the principal id is its <see cref="Organization.Key"/>.</summary>
    Organization,
}

/// <summary>A member's role in a <see cref="Project"/>, applying to the project's repositories.</summary>
public enum ProjectRole
{
    /// <summary><see cref="AccessAction.Read"/>.</summary>
    Viewer,

    /// <summary>Read, Annotate and Deploy.</summary>
    Developer,

    /// <summary>Every action.</summary>
    Admin,
}

/// <summary>What each <see cref="ProjectRole"/> grants.</summary>
public static class ProjectRoles
{
    public static AccessAction ActionsOf(ProjectRole role) => role switch
    {
        ProjectRole.Admin => AccessAction.All,
        ProjectRole.Developer => AccessAction.Read | AccessAction.Annotate | AccessAction.Deploy,
        _ => AccessAction.Read,
    };
}

/// <summary>
/// A named set of users. The two built-in groups are implicit and have no member rows:
/// <see cref="BuiltInNames.Everyone"/> is every caller, anonymous ones included, and
/// <see cref="BuiltInNames.Authenticated"/> is every caller that presented valid credentials.
/// </summary>
public class Group
{
    public int Key { get; set; }

    /// <summary>Unique (case-insensitive); see <see cref="AccessNames"/>.</summary>
    public string? Name { get; set; }

    public string? Description { get; set; }

    public DateTime Created { get; set; }

    /// <summary>Everyone and Authenticated: not deletable, membership implicit.</summary>
    public bool IsBuiltIn { get; set; }
}

/// <summary>The built-in groups and permission targets the upgrade migration seeds (and the admin pages can restore).</summary>
public static class BuiltInNames
{
    public const string Everyone = "Everyone";
    public const string Authenticated = "Authenticated";

    /// <summary>Target: every caller reads every repository (what the server did before the access model).</summary>
    public const string AnyoneReads = "Anyone reads every repository";

    /// <summary>Target: authenticated callers deploy, annotate, delete and manage every repository, with package
    /// ownership still applied inside (what the server did before the access model).</summary>
    public const string AuthenticatedDeploy = "Authenticated users deploy";
}

public class GroupMember
{
    public int Key { get; set; }

    public int GroupKey { get; set; }

    public Group? Group { get; set; }

    /// <summary>The member's <c>BaGetUser.Id</c>.</summary>
    public string? UserId { get; set; }
}

/// <summary>
/// A rule saying which repositories and package IDs its <see cref="PermissionGrant"/>s apply to.
/// </summary>
public class PermissionTarget
{
    public int Key { get; set; }

    /// <summary>Unique (case-insensitive); see <see cref="AccessNames"/>.</summary>
    public string? Name { get; set; }

    /// <summary>Repository names or <c>*</c> globs; the target applies to a repository matching any. Empty matches none.</summary>
    public string[]? RepositoryPatterns { get; set; }

    /// <summary>Package ID globs the target applies to; empty means <c>*</c>.</summary>
    public string[]? IncludePatterns { get; set; }

    /// <summary>Package ID globs the target does not apply to, whatever <see cref="IncludePatterns"/> say.</summary>
    public string[]? ExcludePatterns { get; set; }

    /// <summary>
    /// The <see cref="Project.Key"/> that administers the target. When set, the target applies only to repositories of
    /// that project. A Restrict foreign key to <see cref="Project"/> (nullable); deleting the project deletes its targets first.
    /// </summary>
    public int? ProjectKey { get; set; }

    /// <summary>Seeded by the upgrade migration; restorable, but nothing is special about it once edited.</summary>
    public bool IsBuiltIn { get; set; }

    public DateTime Created { get; set; }
}

public class PermissionGrant
{
    public int Key { get; set; }

    public int TargetKey { get; set; }

    public PermissionTarget? Target { get; set; }

    public PrincipalType PrincipalType { get; set; }

    /// <summary>See <see cref="PrincipalType"/>.</summary>
    public string? PrincipalId { get; set; }

    public AccessAction Actions { get; set; }
}

/// <summary>A user or group with a role in a project.</summary>
public class ProjectMember
{
    public int Key { get; set; }

    public int ProjectKey { get; set; }

    public Project? Project { get; set; }

    /// <summary><see cref="BaGet.Core.PrincipalType.User"/> or <see cref="BaGet.Core.PrincipalType.Group"/>.</summary>
    public PrincipalType PrincipalType { get; set; }

    public string? PrincipalId { get; set; }

    public ProjectRole Role { get; set; }
}

/// <summary>One change to the access model.</summary>
public class AccessAuditEntry
{
    public int Key { get; set; }

    public DateTime Created { get; set; }

    /// <summary>The user name that made the change; null for the server itself.</summary>
    public string? Actor { get; set; }

    /// <summary>What happened, such as <c>group.create</c> or <c>grant.set</c>.</summary>
    public string? Action { get; set; }

    /// <summary>What it happened to (a group, target or repository name).</summary>
    public string? Subject { get; set; }

    /// <summary>JSON with the details (never credentials).</summary>
    public string? Details { get; set; }
}

/// <summary>The one rule for the names of groups, permission targets and projects.</summary>
public static class AccessNames
{
    public const int MaxLength = 128;

    /// <summary>Letters, digits, <c>-</c>, <c>.</c>, <c>_</c> and spaces inside, 1 to 128 characters, starting with a letter or digit.</summary>
    public static bool IsValid(string? name, out string? error)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "A name is required.";
            return false;
        }

        if (name.Length > MaxLength)
        {
            error = $"A name is at most {MaxLength} characters.";
            return false;
        }

        if (!char.IsLetterOrDigit(name[0]) || name != name.Trim())
        {
            error = "A name starts with a letter or digit and has no surrounding spaces.";
            return false;
        }

        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '.' && c != '_' && c != ' ')
            {
                error = "A name uses only letters, digits, spaces, '-', '.' and '_'.";
                return false;
            }
        }

        error = null;
        return true;
    }
}
