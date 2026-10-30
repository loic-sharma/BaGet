using System;
using Ctx = BaGet.Core.AbstractContext<Microsoft.EntityFrameworkCore.DbContext>;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace BaGet.Core;

/// <summary>What a requested Maven path names.</summary>
public enum MavenPathKind
{
    /// <summary>A file of an artifact version (or a checksum of one).</summary>
    File,

    /// <summary><c>maven-metadata.xml</c> at the artifact, version or group level (or a checksum of it).</summary>
    Metadata,
}

/// <summary>A <c>groupId:artifactId</c> and optionally a version.</summary>
/// <param name="GroupId">The group id.</param>
/// <param name="ArtifactId">The artifact id.</param>
/// <param name="Version">The version, or null for the artifact level.</param>
public sealed record MavenCoordinate(string GroupId, string ArtifactId, string? Version)
{
    /// <summary>The name the access model, ownership and the pages call the artifact: <c>group:artifact</c>.</summary>
    public string Name => MavenNames.ArtifactName(GroupId, ArtifactId);
}

/// <summary>A request path below <c>maven/</c>, taken apart.</summary>
public sealed class MavenRequestPath
{
    internal MavenRequestPath(string relativePath, MavenPathKind kind, string fileName, string baseName, string? checksum, IReadOnlyList<MavenCoordinate> candidates, string? groupOnly)
    {
        RelativePath = relativePath;
        Kind = kind;
        FileName = fileName;
        BaseName = baseName;
        ChecksumAlgorithm = checksum;
        Candidates = candidates;
        GroupOnly = groupOnly;
    }

    /// <summary>The path as requested, relative to the repository's <c>maven/</c> address (<c>org/acme/lib/1.0/lib-1.0.jar.sha1</c>).</summary>
    public string RelativePath { get; }

    /// <summary>Whether it is a file or metadata.</summary>
    public MavenPathKind Kind { get; }

    /// <summary>The last segment as requested (<c>a-1.0.jar.sha1</c>).</summary>
    public string FileName { get; }

    /// <summary>The file the request is about: <see cref="FileName"/> without the checksum extension.</summary>
    public string BaseName { get; }

    /// <summary><c>md5</c>, <c>sha1</c>, <c>sha256</c> or <c>sha512</c> when the request is for a checksum; otherwise null.</summary>
    public string? ChecksumAlgorithm { get; }

    /// <summary>
    /// What the directory path can mean. A file has exactly one (group, artifact, version). Metadata has the version level
    /// (when the path is deep enough) first and the artifact level second: <c>a/b/c/maven-metadata.xml</c> is version <c>c</c>
    /// of <c>a:b</c> or the artifact <c>b:c</c> of group <c>a</c>; the stored versions decide.
    /// </summary>
    public IReadOnlyList<MavenCoordinate> Candidates { get; }

    /// <summary>
    /// The same request without the checksum extension (<c>…/lib-1.0.jar</c> for <c>…/lib-1.0.jar.sha1</c>); this path itself when it
    /// is not a checksum request.
    /// </summary>
    public MavenRequestPath Plain
        => ChecksumAlgorithm == null ? this : MavenNames.ParsePath(RelativePath.Substring(0, RelativePath.Length - ChecksumAlgorithm.Length - 1)) ?? this;

    /// <summary>For metadata one level above the artifacts (<c>org/acme/maven-metadata.xml</c>, plugin prefixes): the group; else null.</summary>
    public string? GroupOnly { get; }
}

/// <summary>A file name taken apart.</summary>
/// <param name="Classifier">The classifier, or null.</param>
/// <param name="Extension">The extension without the dot.</param>
/// <param name="SnapshotTimestamp">For a timestamped snapshot build: <c>yyyyMMdd.HHmmss</c>; else null.</param>
/// <param name="SnapshotBuild">For a timestamped snapshot build: the build number; else 0.</param>
public sealed record MavenFileParts(string? Classifier, string Extension, string? SnapshotTimestamp, int SnapshotBuild);

/// <summary>
/// The Maven repository layout (<c>group/as/dirs/artifact/version/artifact-version[-classifier].ext</c>) and the one rule
/// every route, page and service uses to accept a name. Group and artifact ids and versions are case-sensitive; characters are
/// limited to those Maven itself uses, so a path can never leave its folder and a name is the same in every database.
/// </summary>
public static class MavenNames
{
    /// <summary>The storage folder below a repository's prefix.</summary>
    public const string StorageFolder = "maven";

    /// <summary>The metadata file name.</summary>
    public const string MetadataFileName = "maven-metadata.xml";

    /// <summary>The version suffix of a snapshot.</summary>
    public const string SnapshotSuffix = "-SNAPSHOT";

    /// <summary>The checksum extensions, in the order a repository serves them.</summary>
    public static readonly IReadOnlyList<string> ChecksumAlgorithms = new[] { "sha1", "md5", "sha256", "sha512" };

    private static readonly Regex GroupSegment = new(@"^[A-Za-z0-9_][A-Za-z0-9_\-]*\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Identifier = new(@"^[A-Za-z0-9_][A-Za-z0-9_.+\-]*\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ExtensionPattern = new(@"^[A-Za-z][A-Za-z0-9_.+\-]*\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ClassifierPattern = new(@"^[A-Za-z0-9_][A-Za-z0-9_+\-]*\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Timestamp = new(@"^\d{8}\.\d{6}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The name of an artifact in the access model: <c>group:artifact</c>.</summary>
    public static string ArtifactName(string groupId, string artifactId) => groupId + ":" + artifactId;

    /// <summary>Splits <c>group:artifact</c>; false when it is not one.</summary>
    public static bool TryParseArtifactName(string? name, out string groupId, out string artifactId)
    {
        groupId = artifactId = string.Empty;
        if (string.IsNullOrEmpty(name)) return false;

        var colon = name.IndexOf(':');
        if (colon <= 0 || colon == name.Length - 1) return false;

        groupId = name.Substring(0, colon);
        artifactId = name.Substring(colon + 1);

        return IsValidGroupId(groupId) && IsValidArtifactId(artifactId);
    }

    /// <summary>A group id: dot-separated segments of letters, digits, underscore and hyphen (it becomes folders).</summary>
    public static bool IsValidGroupId(string? groupId)
        => !string.IsNullOrEmpty(groupId)
           && groupId.Length <= Ctx.MaxMavenGroupLength
           && groupId.Split('.').All(s => GroupSegment.IsMatch(s));

    /// <summary>An artifact id.</summary>
    public static bool IsValidArtifactId(string? artifactId)
        => !string.IsNullOrEmpty(artifactId)
           && artifactId.Length <= Ctx.MaxMavenArtifactLength
           && Identifier.IsMatch(artifactId);

    /// <summary>A version (a directory name).</summary>
    public static bool IsValidVersion(string? version)
        => !string.IsNullOrEmpty(version)
           && version.Length <= Ctx.MaxMavenVersionLength
           && Identifier.IsMatch(version);

    /// <summary>Whether a version is a snapshot.</summary>
    public static bool IsSnapshot(string? version)
        => version != null && version.EndsWith(SnapshotSuffix, StringComparison.Ordinal);

    /// <summary>The folder of a group: <c>org.apache.maven</c> becomes <c>org/apache/maven</c>.</summary>
    public static string GroupPath(string groupId) => groupId.Replace('.', '/');

    /// <summary>The storage path of a file, relative to the repository's prefix.</summary>
    public static string StoragePath(string groupId, string artifactId, string version, string fileName)
        => StorageFolder + "/" + GroupPath(groupId) + "/" + artifactId + "/" + version + "/" + fileName;

    /// <summary>The path a client uses, relative to the repository's <c>maven/</c> address.</summary>
    public static string ClientPath(string groupId, string artifactId, string version, string fileName)
        => GroupPath(groupId) + "/" + artifactId + "/" + version + "/" + fileName;

    /// <summary>Takes a request path (the part after <c>maven/</c>) apart.</summary>
    /// <returns>Null when the path is not a valid Maven path (too short, an invalid character, a name that cannot be a file).</returns>
    public static MavenRequestPath? ParsePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 2048) return null;

        var segments = path.Split('/');
        if (segments.Length < 2 || segments.Any(s => s.Length == 0)) return null;

        var fileName = segments[^1];
        if (fileName.Length > Ctx.MaxMavenFileNameLength || !Identifier.IsMatch(fileName)) return null;

        var directories = segments.Take(segments.Length - 1).ToArray();
        if (directories.Any(d => d.Length > Ctx.MaxMavenGroupLength || !Identifier.IsMatch(d))) return null;

        var baseName = fileName;
        string? checksum = null;
        foreach (var algorithm in ChecksumAlgorithms)
        {
            if (fileName.Length > algorithm.Length + 1 && fileName.EndsWith("." + algorithm, StringComparison.Ordinal))
            {
                checksum = algorithm;
                baseName = fileName.Substring(0, fileName.Length - algorithm.Length - 1);
                break;
            }
        }

        if (baseName == MetadataFileName)
        {
            var candidates = new List<MavenCoordinate>();
            if (directories.Length >= 3 && TryGroup(directories.Take(directories.Length - 2), out var versionGroup)
                && IsValidArtifactId(directories[^2]) && IsValidVersion(directories[^1]))
            {
                candidates.Add(new MavenCoordinate(versionGroup, directories[^2], directories[^1]));
            }

            if (directories.Length >= 2 && TryGroup(directories.Take(directories.Length - 1), out var artifactGroup) && IsValidArtifactId(directories[^1]))
            {
                candidates.Add(new MavenCoordinate(artifactGroup, directories[^1], null));
            }

            var groupOnly = TryGroup(directories, out var onlyGroup) ? onlyGroup : null;
            if (candidates.Count == 0 && groupOnly == null) return null;

            return new MavenRequestPath(path, MavenPathKind.Metadata, fileName, baseName, checksum, candidates, groupOnly);
        }

        if (directories.Length < 3) return null;
        if (!TryGroup(directories.Take(directories.Length - 2), out var group)
            || !IsValidArtifactId(directories[^2])
            || !IsValidVersion(directories[^1]))
        {
            return null;
        }

        return new MavenRequestPath(
            path,
            MavenPathKind.File,
            fileName,
            baseName,
            checksum,
            new[] { new MavenCoordinate(group, directories[^2], directories[^1]) },
            groupOnly: null);
    }

    private static bool TryGroup(IEnumerable<string> directories, out string groupId)
    {
        var list = directories.ToList();
        groupId = string.Join(".", list);

        // A folder name with a dot would be read as two segments of the group id, so it is not a group folder.
        return list.Count > 0 && list.All(d => GroupSegment.IsMatch(d)) && IsValidGroupId(groupId);
    }

    /// <summary>
    /// Takes the file name <paramref name="fileName"/> of the artifact version apart:
    /// <c>{artifactId}-{version}[-{classifier}].{extension}</c>, where a snapshot's version may be its timestamped form
    /// (<c>1.0-20240101.120000-3</c> for <c>1.0-SNAPSHOT</c>).
    /// </summary>
    /// <returns>Null when the name does not belong to the artifact and version.</returns>
    public static MavenFileParts? ParseFileName(string artifactId, string version, string fileName)
    {
        var prefix = artifactId + "-";
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal)) return null;

        var rest = fileName.Substring(prefix.Length);
        string tail;
        string? stamp = null;
        var build = 0;

        if (rest.StartsWith(version, StringComparison.Ordinal))
        {
            tail = rest.Substring(version.Length);
        }
        else if (IsSnapshot(version))
        {
            var baseVersion = version.Substring(0, version.Length - SnapshotSuffix.Length);
            var stampStart = baseVersion + "-";
            if (!rest.StartsWith(stampStart, StringComparison.Ordinal)) return null;

            var afterBase = rest.Substring(stampStart.Length);

            // yyyyMMdd.HHmmss-N
            if (afterBase.Length < 17 || !Timestamp.IsMatch(afterBase.Substring(0, 15)) || afterBase[15] != '-') return null;

            var digits = 0;
            while (16 + digits < afterBase.Length && char.IsAsciiDigit(afterBase[16 + digits])) digits++;
            if (digits == 0 || digits > 9 || !int.TryParse(afterBase.AsSpan(16, digits), NumberStyles.None, CultureInfo.InvariantCulture, out build) || build < 1) return null;

            stamp = afterBase.Substring(0, 15);
            if (!DateTime.TryParseExact(stamp, "yyyyMMdd.HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return null;

            tail = afterBase.Substring(16 + digits);
        }
        else
        {
            return null;
        }

        string? classifier = null;
        string extension;
        if (tail.StartsWith('.'))
        {
            extension = tail.Substring(1);
        }
        else if (tail.StartsWith('-'))
        {
            var dot = tail.IndexOf('.');
            if (dot < 2) return null;

            classifier = tail.Substring(1, dot - 1);
            extension = tail.Substring(dot + 1);
            if (!ClassifierPattern.IsMatch(classifier)) return null;
        }
        else
        {
            return null;
        }

        if (!ExtensionPattern.IsMatch(extension) || extension.Length > Ctx.MaxMavenExtensionLength) return null;
        if (classifier != null && classifier.Length > Ctx.MaxMavenClassifierLength) return null;

        return new MavenFileParts(classifier, extension, stamp, build);
    }

    /// <summary>The content type served for a file of this extension.</summary>
    public static string ContentTypeOf(string extension) => extension switch
    {
        "pom" or "xml" => "application/xml",
        "jar" or "war" or "ear" => "application/java-archive",
        "module" or "json" => "application/json",
        "asc" or "txt" => "text/plain",
        _ => "application/octet-stream",
    };

    /// <summary>
    /// Orders Maven versions the way Maven does, close enough for <c>latest</c>, <c>release</c> and the listings: numeric parts
    /// numerically, the qualifiers <c>alpha &lt; beta &lt; milestone &lt; rc &lt; snapshot &lt; (release) &lt; sp</c>, others after.
    /// </summary>
    public static int CompareVersions(string? left, string? right)
    {
        var a = Tokens(left ?? string.Empty);
        var b = Tokens(right ?? string.Empty);
        for (var i = 0; i < Math.Max(a.Count, b.Count); i++)
        {
            var result = CompareToken(i < a.Count ? a[i] : null, i < b.Count ? b[i] : null);
            if (result != 0) return result;
        }

        return string.CompareOrdinal(left, right);
    }

    private static List<string> Tokens(string version)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        bool? digit = null;
        foreach (var c in version.ToLowerInvariant())
        {
            if (c is '.' or '-' or '_')
            {
                if (current.Length > 0) tokens.Add(current.ToString());
                current.Clear();
                digit = null;
                continue;
            }

            var isDigit = char.IsAsciiDigit(c);
            if (digit != null && digit != isDigit)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }

            current.Append(c);
            digit = isDigit;
        }

        if (current.Length > 0) tokens.Add(current.ToString());

        // Trailing zeros and release markers do not count: 1.0 == 1.0.0 == 1.0-final.
        while (tokens.Count > 0 && (tokens[^1] is "0" or "final" or "ga" or "release")) tokens.RemoveAt(tokens.Count - 1);

        return tokens;
    }

    // A missing token is the release: 1.0 > 1.0-rc1, 1.0 < 1.0.1, 1.0 < 1.0-sp1.
    private static int CompareToken(string? x, string? y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return -CompareToken(y, null);

        var numeric = long.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out var number);
        if (y == null) return numeric ? (number == 0 ? 0 : 1) : QualifierRank(x) >= QualifierRank(string.Empty) ? 1 : -1;

        var otherNumeric = long.TryParse(y, NumberStyles.None, CultureInfo.InvariantCulture, out var otherNumber);
        if (numeric && otherNumeric) return number.CompareTo(otherNumber);
        if (numeric) return 1;
        if (otherNumeric) return -1;

        var rank = QualifierRank(x).CompareTo(QualifierRank(y));

        return rank != 0 ? rank : string.CompareOrdinal(x, y);
    }

    private static int QualifierRank(string qualifier) => qualifier switch
    {
        "alpha" or "a" => 1,
        "beta" or "b" => 2,
        "milestone" or "m" => 3,
        "rc" or "cr" => 4,
        "snapshot" => 5,
        "" => 6,
        "sp" => 7,
        _ => 8,
    };
}
