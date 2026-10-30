using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BaGet.Core;

/// <summary>
/// Who an API key authenticated, and what it may do.
/// </summary>
public sealed class BaGetPrincipal
{
    /// <summary>
    /// The server's global API key, or any caller when no key is configured: it may do
    /// anything to any package.
    /// </summary>
    public static readonly BaGetPrincipal Superuser = new(
        userId: null,
        userName: null,
        isSuperuser: true,
        scopes: ApiKeyScopes.All,
        packageGlobPattern: "*");

    public BaGetPrincipal(
        string? userId,
        string? userName,
        bool isSuperuser,
        IReadOnlyList<string>? scopes,
        string? packageGlobPattern,
        bool isAdministrator = false,
        IReadOnlyList<int>? repositoryKeys = null,
        bool isInteractiveSession = false)
    {
        IsInteractiveSession = isInteractiveSession;
        RepositoryKeys = repositoryKeys;
        UserId = userId;
        UserName = userName;
        IsSuperuser = isSuperuser;
        IsAdministrator = isAdministrator;
        Scopes = scopes ?? Array.Empty<string>();
        PackageGlobPattern = string.IsNullOrEmpty(packageGlobPattern) ? "*" : packageGlobPattern;
    }

    public string? UserId { get; }

    public string? UserName { get; }

    public bool IsSuperuser { get; }

    /// <summary>
    /// The credential is a person's interactive session (the browser cookie, or a session token of <c>/api/ui/session</c>), not an API key: it
    /// has the rights of the signed-in user with every scope and no repository limit. Account self-service and package ownership screens
    /// are open to it and never to a key.
    /// </summary>
    public bool IsInteractiveSession { get; }

    /// <summary>
    /// The user is an administrator: skips package ownership, never the key's scopes or glob.
    /// </summary>
    public bool IsAdministrator { get; }

    public IReadOnlyList<string> Scopes { get; }

    /// <summary>
    /// The keys of the only repositories the credential may be used in (<see cref="ApiKeyRepository"/>), or null for
    /// every repository: the global key, a browser session and a key without a repository limit.
    /// </summary>
    public IReadOnlyList<int>? RepositoryKeys { get; }

    /// <summary>
    /// Whether the credential may be used in repository <paramref name="repositoryKey"/>. Only a limit of the
    /// credential: what the user may do there is still decided by <see cref="IAccessEvaluator"/>.
    /// </summary>
    public bool AllowsRepository(int repositoryKey) => RepositoryKeys == null || RepositoryKeys.Contains(repositoryKey);

    public string PackageGlobPattern { get; }

    public bool HasScope(string scope) => IsSuperuser || Scopes.Contains(scope, StringComparer.Ordinal);

    public bool MatchesPackage(string? packageId) => IsSuperuser || GlobMatches(PackageGlobPattern, packageId);

    /// <summary>
    /// Match a package ID against a glob in which "*" stands for any run of characters.
    /// Package IDs are case insensitive, so the match is too.
    /// </summary>
    internal static bool GlobMatches(string pattern, string? packageId)
    {
        if (packageId == null) return false;

        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";

        return Regex.IsMatch(packageId, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}