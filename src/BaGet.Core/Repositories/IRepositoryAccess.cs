using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>
/// Which repositories a caller may see and publish to in the web UI. The one place the pages ask, so an access model
/// (groups, permission targets) replaces <see cref="OpenRepositoryAccess"/> without touching a page: the header
/// switcher lists <see cref="ReadableAsync"/>, a repository page answers 404 unless <see cref="CanReadAsync"/>, and
/// the upload page offers <see cref="WritableAsync"/> and opens only for <see cref="CanWriteAsync"/>.
/// </summary>
public interface IRepositoryAccess
{
    /// <summary>
    /// Whether the UI offers repositories beyond <c>default</c> at all. False when the database or the search index can
    /// hold the default repository only (Azure Table, Azure Search): switchers and pickers are then hidden.
    /// </summary>
    bool SupportsMultipleRepositories { get; }

    /// <summary>The repositories (of every format) <paramref name="user"/> may read, ordered by name (<c>default</c> first).</summary>
    Task<IReadOnlyList<RepositoryInfo>> ReadableAsync(ClaimsPrincipal? user, CancellationToken cancellationToken);

    /// <summary>The repositories <paramref name="user"/> may publish to, ordered like <see cref="ReadableAsync"/>.</summary>
    Task<IReadOnlyList<RepositoryInfo>> WritableAsync(ClaimsPrincipal? user, CancellationToken cancellationToken);

    /// <summary>
    /// Whether <paramref name="user"/> may open the upload page of <paramref name="repository"/>. The page shows no
    /// package data of the repository (only the file being staged), so it needs the right to publish, not to read: a
    /// drop box (Deploy without Read) is usable. A visitor who is not signed in pushes with an API key typed into the
    /// page, authorized per package on submit, so the default is <see cref="CanReadAsync"/>.
    /// </summary>
    Task<bool> CanWriteAsync(ClaimsPrincipal? user, RepositoryInfo repository, CancellationToken cancellationToken)
        => CanReadAsync(user, repository, cancellationToken);

    /// <summary>Whether <paramref name="user"/> may read (open pages of) <paramref name="repository"/>.</summary>
    Task<bool> CanReadAsync(ClaimsPrincipal? user, RepositoryInfo repository, CancellationToken cancellationToken);

    /// <summary>
    /// Whether <paramref name="user"/> may read the pages of package <paramref name="packageId"/> in
    /// <paramref name="repository"/>: <see cref="CanReadAsync"/> plus the include/exclude patterns of the Read grants.
    /// The default ignores the package, for an implementation without package-level rules.
    /// </summary>
    Task<bool> CanReadPackageAsync(ClaimsPrincipal? user, RepositoryInfo repository, string packageId, CancellationToken cancellationToken)
        => CanReadAsync(user, repository, cancellationToken);
}

/// <summary>
/// Today's rule: every repository is readable, and writable by whoever the package authorization lets push (which
/// the upload page still checks per package). Replaced when an access model exists.
/// </summary>
public sealed class OpenRepositoryAccess : IRepositoryAccess
{
    private readonly IRepositoryService _repositories;

    public OpenRepositoryAccess(IRepositoryService repositories)
    {
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
    }

    /// <inheritdoc/>
    public bool SupportsMultipleRepositories => _repositories.SupportsMultipleRepositories;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RepositoryInfo>> ReadableAsync(ClaimsPrincipal? user, CancellationToken cancellationToken)
    {
        if (!SupportsMultipleRepositories) return new[] { RepositoryInfo.Default };

        var all = await _repositories.ListAsync(cancellationToken);

        return all
            .OrderBy(r => r.IsDefault ? 0 : 1)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<RepositoryInfo>> WritableAsync(ClaimsPrincipal? user, CancellationToken cancellationToken)
        => ReadableAsync(user, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> CanReadAsync(ClaimsPrincipal? user, RepositoryInfo repository, CancellationToken cancellationToken)
        => Task.FromResult(true);
}
