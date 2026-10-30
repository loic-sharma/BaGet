using System;
using Microsoft.AspNetCore.Identity;

namespace BaGet.Core;

/// <summary>
/// A signed in user. User names and organization names share one namespace, since
/// either can own a package.
/// </summary>
public class BaGetUser : IdentityUser
{
    public DateTime Created { get; set; }

    /// <summary>
    /// May change the server's settings and promote or demote administrators. Holds every action of the access
    /// model in every repository (<see cref="IAccessEvaluator"/>) and skips package ownership, but not the limits of the
    /// API key in use: its scopes and package glob still apply (<c>OwnershipAuthorizationService</c>).
    /// </summary>
    public bool IsAdministrator { get; set; }

    /// <summary>
    /// A self-registered account (<c>Accounts:RequireApproval</c>) that no administrator has enabled yet. It cannot
    /// sign in by any route until the Users page approves it. False for every other account, so existing rows
    /// need no data change.
    /// </summary>
    public bool PendingApproval { get; set; }
}

/// <summary>
/// An account that several users own packages through.
/// </summary>
public class Organization
{
    public int Key { get; set; }

    public string? Name { get; set; }

    public DateTime Created { get; set; }
}

public class OrganizationMember
{
    public int Key { get; set; }

    /// <summary>
    /// Nullable so that the relationship is additive; see <see cref="PackageDeprecation"/>.
    /// </summary>
    public int? OrganizationKey { get; set; }

    public Organization? Organization { get; set; }

    /// <summary>
    /// The member's <see cref="IdentityUser{TKey}.Id"/>.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// An administrator can add and remove members.
    /// </summary>
    public bool IsAdmin { get; set; }
}

/// <summary>
/// An owner of every version of a package ID.
/// </summary>
/// <remarks>
/// A package ID with no owners predates accounts, and only the server's global API key
/// may change it.
/// </remarks>
public class PackageOwner : IRepositoryScoped
{
    public int Key { get; set; }

    /// <summary>
    /// The owning repository; see <see cref="IRepositoryScoped"/>.
    /// </summary>
    public int RepositoryKey { get; set; }

    public string? PackageId { get; set; }

    /// <summary>
    /// A user name or an organization name.
    /// </summary>
    public string? OwnerName { get; set; }

    /// <summary>
    /// Set by the server's administrator to mark the ID as reserved for this owner,
    /// which search reports as "verified".
    /// </summary>
    public bool IsVerified { get; set; }
}

/// <summary>
/// A user's API key. Only a hash of the key is kept.
/// </summary>
public class ApiKey
{
    public int Key { get; set; }

    public string? UserId { get; set; }

    public string? Description { get; set; }

    /// <summary>
    /// The key's SHA-256 hash, as lowercase hex.
    /// </summary>
    public string? HashedKey { get; set; }

    /// <summary>
    /// What the key may do; see <see cref="ApiKeyScopes"/>.
    /// </summary>
    public string[]? Scopes { get; set; }

    /// <summary>
    /// The package IDs the key applies to, with "*" as a wildcard.
    /// </summary>
    public string? PackageGlobPattern { get; set; }

    public DateTime Created { get; set; }

    public DateTime? Expires { get; set; }
}

/// <summary>
/// Limits an <see cref="ApiKey"/> to one repository. A key with no rows may be used in every repository (as every key
/// could before this table existed); a key with rows only in the listed ones: its effective rights are the key's scopes
/// and package glob, intersected with these repositories and with what its user may do there
/// (<see cref="IAccessEvaluator"/>). Rows go with their key (cascade) and with their repository
/// (<see cref="IRepositoryAdministration"/>, which also deletes a key left scoped to nothing).
/// </summary>
public class ApiKeyRepository
{
    public int Key { get; set; }

    /// <summary>The <see cref="ApiKey.Key"/> this row limits.</summary>
    public int ApiKeyKey { get; set; }

    public ApiKey? ApiKey { get; set; }

    /// <summary>The <see cref="Repository.Key"/> the key may be used in. A Restrict foreign key, like <see cref="IRepositoryScoped"/>.</summary>
    public int RepositoryKey { get; set; }
}

/// <summary>
/// The scopes an <see cref="ApiKey"/> may hold, named as nuget.org names them.
/// </summary>
public static class ApiKeyScopes
{
    /// <summary>
    /// Push new package IDs and new versions of owned ones.
    /// </summary>
    public const string Push = "package:push";

    /// <summary>
    /// Push new versions of owned package IDs only.
    /// </summary>
    public const string PushVersion = "package:pushversion";

    /// <summary>
    /// Unlist and relist owned versions, and edit their deprecation and vulnerabilities.
    /// </summary>
    public const string Unlist = "package:unlist";

    /// <summary>
    /// Call the administration REST API (<c>/api/v1</c>). Only a key of an administrator can carry it (the service refuses it
    /// for anyone else, and the API checks the owner is still an administrator on every call). It is deliberately not in
    /// <see cref="All"/>: sessions and npm tokens get <see cref="All"/>, and must never administer the server.
    /// </summary>
    public const string Admin = "admin:api";

    /// <summary>The package scopes: what a session, an npm token and an ordinary key carry.</summary>
    public static readonly string[] All = { Push, PushVersion, Unlist };

    /// <summary>Every scope a key can be created with: <see cref="All"/> and <see cref="Admin"/> (administrators only).</summary>
    public static readonly string[] Assignable = { Push, PushVersion, Unlist, Admin };
}