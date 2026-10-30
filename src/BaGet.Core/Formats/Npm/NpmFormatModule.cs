using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core;

/// <summary>
/// npm on the format layer: the registry protocol under <c>npm/</c> of the repository base (<c>/npm/</c> is never served for
/// <c>default</c>, which is NuGet; a repository <c>web</c> is at <c>/r/web/npm/</c>), tarballs under <c>npm/…</c> in storage.
/// Local repositories hold packages; a remote one caches an upstream registry (<see cref="RemoteNpmRegistry"/>); a virtual one merges
/// members (<see cref="VirtualNpmRegistry"/>). See <see cref="INpmRegistry"/>.
/// </summary>
public class NpmFormatModule : IFormatModule
{
    /// <inheritdoc/>
    public RepositoryFormat Format => RepositoryFormat.Npm;

    /// <inheritdoc/>
    public string DisplayName => "npm";

    /// <inheritdoc/>
    public string RoutePrefix => "npm";

    /// <inheritdoc/>
    public string FeedPath => "npm/";

    /// <inheritdoc/>
    public IReadOnlyCollection<RepositoryKind> CreatableKinds { get; } = new[] { RepositoryKind.Local, RepositoryKind.Remote };

    /// <inheritdoc/>
    public bool SupportsVirtual => true;

    /// <inheritdoc/>
    public IReadOnlyList<string> StorageFolders { get; } = new[] { NpmNames.StorageFolder };

    /// <inheritdoc/>
    /// <remarks>None: an npm repository is a name.</remarks>
    public IReadOnlyList<FormatCreateField> CreateFields { get; } = Array.Empty<FormatCreateField>();

    /// <inheritdoc/>
    public string? CreateSettings(IReadOnlyDictionary<string, string?> fields) => null;

    /// <inheritdoc/>
    public IReadOnlyList<FormatLink> RepositoryLinks(RepositoryInfo repository) => new[]
    {
        FormatLink.ToScreen("browse", "Browse", "NpmBrowse", "Packages"),
        FormatLink.ToScreen("upload", "Upload", "NpmBrowse", "Upload", needsUploads: true),
        FormatLink.ToPath("registry", "Registry", "npm/"),
    };

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<int, int>> CountArtifactsAsync(IContext context, CancellationToken cancellationToken)
        => await context.NpmPackages!
            .IgnoreQueryFilters()
            .GroupBy(p => p.RepositoryKey)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<FormatUsage> GetUsageAsync(IContext context, int repositoryKey, CancellationToken cancellationToken)
    {
        var packages = await context.NpmPackages!.IgnoreQueryFilters().CountAsync(e => e.RepositoryKey == repositoryKey, cancellationToken).ConfigureAwait(false);

        return new FormatUsage(packages, PendingWork: false);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// An empty repository has no packages, so versions and dist-tags (which cascade from a package) are gone already; what
    /// is left is the owner rows, which outlive their package as NuGet's do.
    /// </remarks>
    public async Task StagePurgeAsync(IContext context, int repositoryKey, CancellationToken cancellationToken)
    {
        context.NpmPackages!.RemoveRange(await context.NpmPackages!.IgnoreQueryFilters().Where(e => e.RepositoryKey == repositoryKey).ToListAsync(cancellationToken).ConfigureAwait(false));
        context.PackageOwners!.RemoveRange(await context.PackageOwners!.IgnoreQueryFilters().Where(e => e.RepositoryKey == repositoryKey).ToListAsync(cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>npm's <see cref="IFormatSearchProvider"/>: packages whose name contains the query (<see cref="INpmRegistry.SearchAsync"/>).</summary>
public class NpmFormatSearchProvider : IFormatSearchProvider
{
    private readonly INpmRegistry _registry;

    /// <param name="registry">The registry of the request's repository.</param>
    public NpmFormatSearchProvider(INpmRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <inheritdoc/>
    public RepositoryFormat Format => RepositoryFormat.Npm;

    /// <inheritdoc/>
    public async Task<FormatSearchPage> SearchAsync(FormatSearchQuery query, CancellationToken cancellationToken)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));

        var page = await _registry.SearchAsync(query.Query, query.Skip, query.Take, canRead: null, cancellationToken).ConfigureAwait(false);

        return new FormatSearchPage(
            page.Total,
            page.Hits
                .Where(h => query.IncludePrerelease || h.Version == null || !NpmNames.IsPrerelease(h.Version))
                .Select(h => new FormatSearchHit(h.Name, h.Version, h.Description, Downloads: 0))
                .ToList());
    }
}

/// <summary>
/// Issues the tokens <c>npm login</c> stores (<c>_authToken</c>). A token is an API key of the user (so it is listed and revoked
/// on the account's API keys page and carries the same rules), limited to the repository it was issued for. Implemented by the
/// accounts feature; a host without accounts has none, and its npm clients use the global API key as the token.
/// </summary>
public interface INpmTokenService
{
    /// <summary>
    /// Checks the password and issues a token for <paramref name="repository"/>; no token when the user name or password is
    /// wrong, the account is locked out, or the account needs more than a password (two-factor authentication): such a user
    /// creates an API key on the account page instead.
    /// </summary>
    Task<NpmLoginResult> LoginAsync(string? userName, string? password, RepositoryInfo repository, CancellationToken cancellationToken);

    /// <summary>Revokes <paramref name="token"/> when it is an API key of <paramref name="caller"/> (<c>npm logout</c>).</summary>
    Task<bool> RevokeAsync(BaGetPrincipal caller, string token, CancellationToken cancellationToken);
}

/// <summary>The answer to <see cref="INpmTokenService.LoginAsync"/>.</summary>
/// <param name="Token">The token, or null.</param>
/// <param name="Error">Why there is none.</param>
/// <param name="LockedOut">Whether the account is locked out (the client is told to wait).</param>
public sealed record NpmLoginResult(string? Token, string? Error = null, bool LockedOut = false);
