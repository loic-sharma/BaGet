using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>
/// A repository as the administration pages list it.
/// </summary>
/// <param name="Key">The <see cref="Repository.Key"/>.</param>
/// <param name="Name">The <see cref="Repository.Name"/>.</param>
/// <param name="Format">The package format served.</param>
/// <param name="Kind">How the repository gets its content.</param>
/// <param name="AnonymousRead">Whether requests without credentials may read it.</param>
/// <param name="Created">When the repository was created (UTC).</param>
/// <param name="PackageCount">How many package versions it holds.</param>
public sealed record RepositorySummary(
    int Key,
    string Name,
    RepositoryFormat Format,
    RepositoryKind Kind,
    bool AnonymousRead,
    DateTime Created,
    int PackageCount)
{
    /// <summary>
    /// Whether this is the <see cref="Repository.DefaultName"/> repository, which can be neither renamed nor deleted.
    /// </summary>
    public bool IsDefault => Key == Repository.DefaultKey;
}

/// <summary>
/// The one rule that narrows a repository list by format, kind or a name fragment, shared by the Repositories page and
/// <c>GET /api/v1/repositories</c> so the two cannot answer differently.
/// </summary>
public static class RepositorySummaryFilter
{
    /// <summary>
    /// Keeps the repositories whose format name and kind name equal <paramref name="format"/> and <paramref name="kind"/>
    /// (ignoring case) and whose name contains <paramref name="nameFragment"/> (ignoring case); an empty filter keeps everything.
    /// </summary>
    public static IEnumerable<RepositorySummary> Apply(IEnumerable<RepositorySummary> repositories, string? format, string? kind, string? nameFragment)
        => repositories
            .Where(r => string.IsNullOrEmpty(format) || string.Equals(r.Format.ToString(), format, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrEmpty(kind) || string.Equals(r.Kind.ToString(), kind, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrEmpty(nameFragment) || r.Name.Contains(nameFragment, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Lists, edits and deletes repositories (creating goes through <see cref="IRepositoryService.CreateAsync(string, RepositoryFormat, RepositoryKind, RemoteRepositorySettings?, CancellationToken)"/>). What the
/// administrator pages use; writes go through the database context, so they are refused while the
/// <see cref="MaintenanceGate"/> is closed.
/// </summary>
public interface IRepositoryAdministration
{
    /// <summary>
    /// Every repository with its package count, ordered by name; the default one included.
    /// </summary>
    Task<IReadOnlyList<RepositorySummary>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Finds a repository by exact name.
    /// </summary>
    /// <returns>The repository, or null.</returns>
    Task<RepositorySummary?> FindAsync(string? name, CancellationToken cancellationToken);

    /// <summary>
    /// Sets <see cref="Repository.AnonymousRead"/>, the shortcut for the <c>Everyone</c> group's Read on the repository.
    /// Off, the group's Read grants no longer apply there (callers need a Read grant of their own). On, it applies
    /// again, and when no target grants <c>Everyone</c> Read on the repository any more (an administrator deleted
    /// the seeded one) a target <c>Anonymous read {name}</c> is created, so the flag never claims what the grants deny.
    /// Audited. A repository is never renamed: its name is in URLs and storage paths.
    /// </summary>
    /// <param name="name">The repository.</param>
    /// <param name="anonymousRead">The new value.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="actor">The user name recorded in the audit log.</param>
    /// <exception cref="KeyNotFoundException">No such repository.</exception>
    Task SetAnonymousReadAsync(string name, bool anonymousRead, CancellationToken cancellationToken, string? actor = null);

    /// <summary>
    /// Deletes a repository that holds no packages and no submission awaiting validation, together with its catalog
    /// entries, owner rows, validations and abuse reports, and every file it kept in storage (<c>repos/{name}/</c>) when
    /// the store can list its content.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such repository.</exception>
    /// <exception cref="RepositoryProtectedException">It is the default repository.</exception>
    /// <exception cref="RepositoryNotEmptyException">It still holds packages (delete them first) or a submission awaiting
    /// validation (wait for it or let it fail). Its catalog entries, owners, finished validations and abuse reports are
    /// history and are removed with the repository.</exception>
    Task DeleteAsync(string name, CancellationToken cancellationToken);
}

/// <summary>
/// The default repository cannot be renamed or deleted.
/// </summary>
public sealed class RepositoryProtectedException : InvalidOperationException
{
    public RepositoryProtectedException()
        : base($"The '{Repository.DefaultName}' repository is served at the root and cannot be deleted.")
    { }
}

/// <summary>
/// The repository still has content, so it cannot be deleted.
/// </summary>
/// <summary>
/// Thrown when a repository that virtual repositories list as a member is deleted: they would lose a member.
/// </summary>
public sealed class RepositoryReferencedException : InvalidOperationException
{
    public RepositoryReferencedException(string name, IReadOnlyList<string> virtualRepositories)
        : base($"The repository '{name}' is a member of the virtual repositor{(virtualRepositories.Count == 1 ? "y" : "ies")} {string.Join(", ", virtualRepositories.Select(v => "'" + v + "'"))}. Remove it from {(virtualRepositories.Count == 1 ? "it" : "them")} first.")
    {
        VirtualRepositories = virtualRepositories;
    }

    /// <summary>The virtual repositories that list the repository.</summary>
    public IReadOnlyList<string> VirtualRepositories { get; }
}

public sealed class RepositoryNotEmptyException : InvalidOperationException
{
    public RepositoryNotEmptyException(string name, bool awaitingValidation = false, bool remote = false)
        : base(awaitingValidation
            ? $"The repository '{name}' still has submissions awaiting validation. Wait for them to finish; its history (catalog entries, owners) is removed with the repository."
            : remote
                ? $"The repository '{name}' still holds cached packages. Purge its cache first; its history (catalog entries) is removed with the repository."
                : $"The repository '{name}' still holds packages. Delete them first; its history (catalog entries, owners) is removed with the repository.")
    { }
}
