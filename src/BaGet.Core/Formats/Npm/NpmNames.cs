using System;
using System.Text.RegularExpressions;
using NuGet.Versioning;

namespace BaGet.Core;

/// <summary>
/// The rules for npm package names, versions, tarball file names and storage paths: one definition, used by the routes, the
/// registry service, the pages and the storage layout, so a name that is accepted in one place is accepted everywhere and a
/// name that could become a path outside the package's folder is accepted nowhere.
/// </summary>
public static class NpmNames
{
    /// <summary>The folder (below the repository's storage prefix) that holds tarballs: <c>npm/{name}/-/{file}</c>.</summary>
    public const string StorageFolder = "npm";

    /// <summary>The longest name npm accepts, scope included.</summary>
    public const int MaxLength = AbstractContext<Microsoft.EntityFrameworkCore.DbContext>.MaxNpmNameLength;

    // One segment of a name: URL safe, lower case, not starting with '.', '_' or '-' ('-' is the literal segment of the
    // registry's own routes: /-/ping, /{name}/-/{file}). npm allows more in legacy names; new names are held to this.
    private static readonly Regex Part = new("^[a-z0-9~][a-z0-9._~-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // The same shape for a name that is only looked up: npm's legacy names (registry.npmjs.org still serves JSONStream,
    // Base64, ...) have upper case letters. Nothing else is relaxed, so the name stays a safe path segment.
    private static readonly Regex LookupPart = new("^[A-Za-z0-9~][A-Za-z0-9._~-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TagShape = new("^[A-Za-z0-9._~-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] Reserved = { "node_modules", "favicon.ico" };

    /// <summary>
    /// Validates <paramref name="name"/> as a name to publish or look up: <c>left-pad</c> or <c>@scope/name</c>, lower case,
    /// at most <see cref="MaxLength"/> characters.
    /// </summary>
    /// <param name="name">The name as the client wrote it (an encoded <c>%2f</c> is decoded first).</param>
    /// <param name="normalized">The decoded name when valid.</param>
    /// <param name="error">Why not, when invalid.</param>
    public static bool TryParse(string? name, out string normalized, out string error)
        => TryParse(name, legacy: false, out normalized, out error);

    /// <summary>
    /// Validates <paramref name="name"/> as a name to look up (read, proxy to an upstream, purge from a cache): like
    /// <see cref="TryParse(string?, out string, out string)"/> but upper case letters are accepted, since packages published
    /// long ago under such names still exist upstream. The case is kept (an upstream registry is asked for the name as
    /// written). Publishing and every other change keep the strict rule.
    /// </summary>
    /// <param name="name">The name as the client wrote it (an encoded <c>%2f</c> is decoded first).</param>
    /// <param name="normalized">The decoded name when valid.</param>
    /// <param name="error">Why not, when invalid.</param>
    public static bool TryParseLookup(string? name, out string normalized, out string error)
        => TryParse(name, legacy: true, out normalized, out error);

    private static bool TryParse(string? name, bool legacy, out string normalized, out string error)
    {
        var part = legacy ? LookupPart : Part;
        normalized = string.Empty;
        error = string.Empty;

        if (string.IsNullOrEmpty(name))
        {
            error = "A package name is required.";
            return false;
        }

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(name);
        }
        catch (UriFormatException)
        {
            error = "The package name is not valid.";
            return false;
        }

        if (decoded.Length > MaxLength)
        {
            error = $"A package name has at most {MaxLength} characters.";
            return false;
        }

        if (!legacy && decoded != decoded.ToLowerInvariant())
        {
            error = "A package name must be lower case.";
            return false;
        }

        string unscoped;
        if (decoded.StartsWith('@'))
        {
            var slash = decoded.IndexOf('/');
            if (slash < 2 || slash == decoded.Length - 1 || decoded.IndexOf('/', slash + 1) >= 0)
            {
                error = "A scoped package name looks like @scope/name.";
                return false;
            }

            if (!part.IsMatch(decoded.Substring(1, slash - 1)))
            {
                error = "The scope of a package name has only lower case letters, digits and . _ ~ - characters.";
                return false;
            }

            unscoped = decoded.Substring(slash + 1);
        }
        else
        {
            unscoped = decoded;
        }

        if (!part.IsMatch(unscoped) || unscoped.StartsWith('.') || unscoped.StartsWith('_'))
        {
            error = "A package name has only lower case letters, digits and . _ ~ - characters, and starts with a letter or digit.";
            return false;
        }

        if (Array.IndexOf(Reserved, unscoped.ToLowerInvariant()) >= 0)
        {
            error = $"'{unscoped}' is not allowed as a package name.";
            return false;
        }

        normalized = decoded;
        return true;
    }

    /// <summary>The name without its scope: <c>name</c> for <c>@scope/name</c>.</summary>
    public static string Unscoped(string name)
    {
        var slash = name.IndexOf('/');

        return name.StartsWith('@') && slash >= 0 ? name.Substring(slash + 1) : name;
    }

    /// <summary>The scope with its <c>@</c> (<c>@scope</c>), or null for an unscoped name.</summary>
    public static string? Scope(string name)
    {
        var slash = name.IndexOf('/');

        return name.StartsWith('@') && slash >= 0 ? name.Substring(0, slash) : null;
    }

    /// <summary>The tarball's file name: <c>name-1.0.0.tgz</c> (the scope is not repeated).</summary>
    public static string TarballFileName(string name, string version) => $"{Unscoped(name)}-{version}.tgz";

    /// <summary>The storage path of a tarball, relative to the repository's prefix: <c>npm/@scope/name/-/name-1.0.0.tgz</c>.</summary>
    public static string TarballPath(string name, string version) => $"{StorageFolder}/{name}/-/{TarballFileName(name, version)}";

    /// <summary>
    /// The version a tarball file name carries for <paramref name="name"/> (<c>name-1.0.0.tgz</c> gives <c>1.0.0</c>), or
    /// false when the file name does not belong to the package or carries no valid version.
    /// </summary>
    public static bool TryParseTarballFile(string name, string? file, out string version)
    {
        version = string.Empty;
        var prefix = Unscoped(name) + "-";
        if (file == null || !file.StartsWith(prefix, StringComparison.Ordinal) || !file.EndsWith(".tgz", StringComparison.Ordinal)) return false;

        var middle = file.Substring(prefix.Length, file.Length - prefix.Length - ".tgz".Length);
        if (!TryParseVersion(middle, out var parsed)) return false;

        version = parsed;
        return true;
    }

    /// <summary>Whether <paramref name="version"/> is a semantic version (3 numeric parts, optional prerelease and build), written in its canonical form.</summary>
    public static bool TryParseVersion(string? version, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(version) || version.Length > AbstractContext<Microsoft.EntityFrameworkCore.DbContext>.MaxNpmVersionLength) return false;

        if (!SemanticVersion.TryParse(version, out var parsed)) return false;

        // Only the form the client wrote: " 1.0.0" or "v1.0.0" would name a file the client does not ask for.
        if (parsed.ToFullString() != version) return false;

        normalized = version;
        return true;
    }

    /// <summary>The version without build metadata, which npm ignores when it compares versions (<c>1.0.0+a</c> is <c>1.0.0</c>), and in lower case (<c>1.0.0-Beta</c> and <c>1.0.0-beta</c> share a file on a case-insensitive store, so they are one version).</summary>
    public static string Comparable(string version)
        => SemanticVersion.TryParse(version, out var parsed) ? parsed.ToNormalizedString().ToLowerInvariant() : version.ToLowerInvariant();

    /// <summary>Whether the version carries a prerelease label.</summary>
    public static bool IsPrerelease(string version)
        => SemanticVersion.TryParse(version, out var parsed) && parsed.IsPrerelease;

    /// <summary>
    /// The highest of <paramref name="versions"/> by semantic version order, preferring versions without a prerelease label
    /// (what <c>latest</c> should point at); null when empty.
    /// </summary>
    public static string? Highest(System.Collections.Generic.IEnumerable<string> versions, bool stableFirst = true)
    {
        string? best = null;
        SemanticVersion? bestParsed = null;
        string? bestStable = null;
        SemanticVersion? bestStableParsed = null;

        foreach (var version in versions)
        {
            if (!SemanticVersion.TryParse(version, out var parsed)) continue;

            if (bestParsed == null || parsed > bestParsed)
            {
                best = version;
                bestParsed = parsed;
            }

            if (!parsed.IsPrerelease && (bestStableParsed == null || parsed > bestStableParsed))
            {
                bestStable = version;
                bestStableParsed = parsed;
            }
        }

        return stableFirst && bestStable != null ? bestStable : best;
    }

    /// <summary>Whether <paramref name="tag"/> can be a dist-tag: 1 to 128 URL-safe characters that are not a version.</summary>
    public static bool IsValidTag(string? tag, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrEmpty(tag) || tag.Length > AbstractContext<Microsoft.EntityFrameworkCore.DbContext>.MaxNpmTagLength)
        {
            error = "A tag has 1 to 128 characters.";
            return false;
        }

        if (!TagShape.IsMatch(tag))
        {
            error = "A tag has only letters, digits and . _ ~ - characters.";
            return false;
        }

        // npm refuses a tag that is a version, since "npm install pkg@1.0.0" must mean the version.
        if (SemanticVersion.TryParse(tag, out _))
        {
            error = "A tag cannot be a version.";
            return false;
        }

        return true;
    }
}
