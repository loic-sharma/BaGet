using System;

namespace BaGet.Core;

/// <summary>
/// The one definition of where a repository keeps its files. The <see cref="Repository.DefaultName"/> repository
/// keeps the layout that predates repositories (<c>packages/…</c>, <c>symbols/…</c>, <c>catalog/…</c>,
/// <c>validation/…</c>, <c>staging/…</c>), so existing stores need no migration; every other repository nests the
/// same layout under <c>repos/{name}/</c>.
/// </summary>
/// <remarks>
/// The name becomes a folder, so a non-default name is re-validated here with <see cref="RepositoryNames"/>
/// (defence in depth: the repository service already validates it) and never concatenated unchecked.
/// </remarks>
public static class RepositoryStorage
{
    /// <summary>
    /// The folder that holds every non-default repository.
    /// </summary>
    public const string ReposFolder = "repos";

    /// <summary>
    /// The prefix to put before a storage path: empty for the default repository, else <c>repos/{name}/</c>.
    /// </summary>
    /// <exception cref="ArgumentException">A non-default repository whose name is not valid.</exception>
    public static string Prefix(RepositoryInfo repository)
    {
        if (repository == null) throw new ArgumentNullException(nameof(repository));
        if (repository.IsDefault) return string.Empty;

        if (!RepositoryNames.IsValid(repository.Name, out var error))
        {
            throw new ArgumentException($"Repository name '{repository.Name}' cannot be used in a storage path: {error}", nameof(repository));
        }

        return $"{ReposFolder}/{repository.Name}/";
    }

    /// <summary>
    /// Scopes a forward-slash storage <paramref name="path"/> (<c>catalog/x.json</c>) to <paramref name="repository"/>.
    /// </summary>
    public static string Scope(RepositoryInfo repository, string path) => Prefix(repository) + path;

    /// <summary>
    /// The folder segments to put before the segments of a path built with <see cref="System.IO.Path.Combine(string[])"/>:
    /// none for the default repository, else <c>repos</c> and the name. Keeps default paths byte-identical to before.
    /// </summary>
    public static string[] Segments(RepositoryInfo repository)
    {
        var prefix = Prefix(repository);

        return prefix.Length == 0 ? Array.Empty<string>() : new[] { ReposFolder, repository.Name };
    }
}
