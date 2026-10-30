using System;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Core;

/// <summary>
/// The repository the current request, job or command works in. Scoped. Defaults to the
/// <see cref="Repository.DefaultName"/> repository, so code that never selects one behaves as before repositories.
/// </summary>
/// <remarks>
/// Whoever decides the repository (the routing middleware from <c>/r/{repo}</c>, a background worker, the CLI)
/// calls <see cref="Use"/> once, before anything queries the database: the database context filters its
/// repository-scoped tables on <see cref="Current"/>.
/// <see cref="RepositoryScopeExtensions.CreateRepositoryScope"/> creates a DI scope already pinned to a
/// repository, for code outside a request.
/// </remarks>
public interface IRepositoryContext
{
    /// <summary>
    /// The repository in effect.
    /// </summary>
    RepositoryInfo Current { get; }

    /// <summary>
    /// Selects the repository for the rest of this scope.
    /// </summary>
    void Use(RepositoryInfo repository);
}

/// <summary>
/// The scoped <see cref="IRepositoryContext"/>.
/// </summary>
public sealed class RepositoryContext : IRepositoryContext
{
    private volatile RepositoryInfo _current = RepositoryInfo.Default;

    /// <inheritdoc/>
    public RepositoryInfo Current => _current;

    /// <inheritdoc/>
    public void Use(RepositoryInfo repository)
    {
        _current = repository ?? throw new ArgumentNullException(nameof(repository));
    }
}

public static class RepositoryScopeExtensions
{
    /// <summary>
    /// Creates a DI scope whose <see cref="IRepositoryContext"/> is already set to <paramref name="repository"/>.
    /// For background services and commands that work on a given repository; dispose it when done.
    /// </summary>
    public static IServiceScope CreateRepositoryScope(this IServiceScopeFactory factory, RepositoryInfo repository)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        if (repository == null) throw new ArgumentNullException(nameof(repository));

        var scope = factory.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepositoryContext>().Use(repository);
        return scope;
    }
}

/// <summary>
/// The context used when nothing selected a repository: always the default one.
/// </summary>
internal sealed class DefaultRepositoryContext : IRepositoryContext
{
    public static readonly DefaultRepositoryContext Instance = new();

    public RepositoryInfo Current => RepositoryInfo.Default;

    public void Use(RepositoryInfo repository)
        => throw new InvalidOperationException("No repository scope is available; create one with CreateRepositoryScope.");
}
