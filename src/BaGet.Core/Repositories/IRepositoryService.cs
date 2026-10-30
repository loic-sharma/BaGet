using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>
/// Looks up and creates repositories. What routing uses to turn <c>/r/{name}</c> into the
/// <see cref="RepositoryInfo"/> it passes to <see cref="IRepositoryContext.Use"/>.
/// </summary>
public interface IRepositoryService
{
    /// <summary>
    /// Whether the database can hold repositories beyond <see cref="Repository.DefaultName"/>. False for a
    /// non-relational database (Azure Table): there only the default repository exists.
    /// </summary>
    bool SupportsMultipleRepositories { get; }

    /// <summary>
    /// Finds a repository by name (exact, as names are lowercase).
    /// </summary>
    /// <returns>The repository, or null. The default repository is always found.</returns>
    Task<RepositoryInfo?> FindAsync(string? name, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a repository by key; for code that holds a row's <see cref="IRepositoryScoped.RepositoryKey"/> and must
    /// work in that repository (a queued validation, for one).
    /// </summary>
    /// <returns>The repository, or null. The default repository is always found.</returns>
    Task<RepositoryInfo?> FindAsync(int key, CancellationToken cancellationToken);

    /// <summary>
    /// Every repository, ordered by name; the default one included.
    /// </summary>
    Task<IReadOnlyList<RepositoryInfo>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates a repository. A <see cref="RepositoryKind.Remote"/> one needs its upstream: use
    /// <see cref="CreateAsync(string, RepositoryFormat, RepositoryKind, RemoteRepositorySettings?, CancellationToken)"/>.
    /// </summary>
    /// <exception cref="System.ArgumentException">The name is invalid per <see cref="RepositoryNames"/>.</exception>
    /// <exception cref="System.NotSupportedException">The database cannot hold repositories, or the format/kind is not available yet.</exception>
    /// <exception cref="RepositoryExistsException">The name is taken.</exception>
    Task<RepositoryInfo> CreateAsync(
        string name,
        RepositoryFormat format,
        RepositoryKind kind,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates a repository; <paramref name="remote"/> is the upstream of a <see cref="RepositoryKind.Remote"/> one
    /// (required for it, refused for the other kinds). The password is stored protected.
    /// </summary>
    /// <exception cref="System.ArgumentException">The name is invalid, or the upstream settings are missing, unneeded or invalid (the message lists the problems).</exception>
    /// <exception cref="System.NotSupportedException">The database cannot hold repositories, or the format/kind is not available yet (virtual).</exception>
    /// <exception cref="System.InvalidOperationException">A password was given and nothing can protect it (no data protection on the host).</exception>
    /// <exception cref="RepositoryExistsException">The name is taken.</exception>
    Task<RepositoryInfo> CreateAsync(
        string name,
        RepositoryFormat format,
        RepositoryKind kind,
        RemoteRepositorySettings? remote,
        CancellationToken cancellationToken);

    /// <summary>
    /// Like <see cref="CreateAsync(string, RepositoryFormat, RepositoryKind, RemoteRepositorySettings?, CancellationToken)"/>
    /// with <see cref="Repository.AnonymousRead"/> chosen by the caller. The overload without it creates a remote
    /// repository with credentials (a user name or password) readable only by authenticated callers, since otherwise
    /// everything its credentials can fetch would be served to anyone; every other repository starts anonymously
    /// readable, like the default one.
    /// </summary>
    Task<RepositoryInfo> CreateAsync(
        string name,
        RepositoryFormat format,
        RepositoryKind kind,
        RemoteRepositorySettings? remote,
        bool anonymousRead,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates a repository of a format whose <see cref="IFormatModule"/> asks for fields of its own
    /// (<see cref="IFormatModule.CreateFields"/>): <paramref name="fields"/> are turned into the repository's settings by
    /// <see cref="IFormatModule.CreateSettings"/>. Not for remote repositories, which take their upstream through
    /// <see cref="CreateAsync(string, RepositoryFormat, RepositoryKind, RemoteRepositorySettings?, bool, CancellationToken)"/>.
    /// </summary>
    /// <exception cref="System.ArgumentException">The name or a field is invalid.</exception>
    /// <exception cref="System.NotSupportedException">The database cannot hold repositories, or no module of the format offers the kind.</exception>
    /// <exception cref="RepositoryExistsException">The name is taken.</exception>
    Task<RepositoryInfo> CreateAsync(
        string name,
        RepositoryFormat format,
        RepositoryKind kind,
        System.Collections.Generic.IReadOnlyDictionary<string, string?> fields,
        bool anonymousRead,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates the default repository if the database has none. Idempotent; called at startup, after migrations.
    /// </summary>
    Task EnsureDefaultAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A repository with that name exists.
/// </summary>
public sealed class RepositoryExistsException : System.InvalidOperationException
{
    public RepositoryExistsException(string name)
        : base($"A repository named '{name}' already exists.")
    { }
}
