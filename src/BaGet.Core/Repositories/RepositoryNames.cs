using System;
using System.Collections.Generic;

namespace BaGet.Core;

/// <summary>
/// The one definition of what a repository may be called. A name is a URL segment (<c>/r/{name}/</c>) and a
/// storage folder, so it is restricted to lowercase <c>[a-z0-9-._]</c>, 1 to 64 characters, and may not
/// collide with a path BaGet itself serves.
/// </summary>
public static class RepositoryNames
{
    public const int MaxLength = 64;

    /// <summary>
    /// Names that would shadow a route or static path. Compared case-insensitively.
    /// </summary>
    public static readonly IReadOnlyCollection<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "r", "api", "v2", "v3", "admin", "account", "packages", "profiles", "symbols", "catalog",
        "upload", "health", "css", "js", "lib", "images", "favicon.ico", "_content", "_framework",
    };

    /// <summary>
    /// Checks <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The candidate name.</param>
    /// <param name="error">Why it is refused, or null.</param>
    /// <returns>Whether the name may be used.</returns>
    public static bool IsValid(string? name, out string? error)
    {
        if (string.IsNullOrEmpty(name))
        {
            error = "A repository name is required.";
            return false;
        }

        if (name.Length > MaxLength)
        {
            error = $"A repository name is at most {MaxLength} characters.";
            return false;
        }

        foreach (var c in name)
        {
            if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '-' && c != '.' && c != '_')
            {
                error = "A repository name uses only lowercase letters, digits, '-', '.' and '_'.";
                return false;
            }
        }

        if (name[0] == '.' || name[name.Length - 1] == '.' || name.Contains(".."))
        {
            error = "A repository name cannot start or end with '.' or contain '..'.";
            return false;
        }

        if (((HashSet<string>)Reserved).Contains(name))
        {
            error = $"'{name}' is reserved.";
            return false;
        }

        error = null;
        return true;
    }

    /// <inheritdoc cref="IsValid(string?, out string?)"/>
    public static bool IsValid(string? name) => IsValid(name, out _);
}
