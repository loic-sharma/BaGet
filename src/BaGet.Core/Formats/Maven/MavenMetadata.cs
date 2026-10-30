using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace BaGet.Core;

/// <summary>A stored file as the metadata generator needs it.</summary>
/// <param name="FileName">The file name.</param>
/// <param name="Classifier">The classifier, or null.</param>
/// <param name="Extension">The extension.</param>
/// <param name="SnapshotTimestamp">The snapshot build's timestamp (<c>yyyyMMdd.HHmmss</c>), or null.</param>
/// <param name="SnapshotBuild">The snapshot build number, 0 when not a timestamped build.</param>
/// <param name="Created">When it was stored.</param>
public sealed record MavenFileInfo(string FileName, string? Classifier, string Extension, string? SnapshotTimestamp, int SnapshotBuild, DateTime Created);

/// <summary>
/// Generates <c>maven-metadata.xml</c> from what is stored; it is never kept as a file, so it can never disagree with the
/// versions (and a client's own copy, uploaded by <c>mvn deploy</c>, is not needed). The same bytes are hashed for the
/// <c>.sha1</c> / <c>.md5</c> companions.
/// </summary>
public static class MavenMetadata
{
    /// <summary>The artifact level: every version, with <c>latest</c> and <c>release</c>.</summary>
    /// <param name="groupId">The group id.</param>
    /// <param name="artifactId">The artifact id.</param>
    /// <param name="versions">The stored versions.</param>
    /// <param name="lastUpdated">When the artifact last changed.</param>
    /// <param name="latest">The version to name as <c>latest</c> when it is one of <paramref name="versions"/> (a virtual repository follows its member order); the highest otherwise.</param>
    /// <param name="release">The version to name as <c>release</c> when it is one of <paramref name="versions"/>; the highest release otherwise.</param>
    public static string ForArtifact(string groupId, string artifactId, IEnumerable<string> versions, DateTime lastUpdated, string? latest = null, string? release = null)
    {
        var ordered = versions.Distinct(StringComparer.Ordinal).OrderBy(v => v, Comparer<string>.Create(MavenNames.CompareVersions)).ToList();
        latest = latest != null && ordered.Contains(latest, StringComparer.Ordinal) ? latest : ordered.Count > 0 ? ordered[^1] : null;
        release = release != null && ordered.Contains(release, StringComparer.Ordinal) ? release : ordered.LastOrDefault(v => !MavenNames.IsSnapshot(v));

        var versioning = new XElement("versioning");
        if (latest != null) versioning.Add(new XElement("latest", latest));
        if (release != null) versioning.Add(new XElement("release", release));

        versioning.Add(new XElement("versions", ordered.Select(v => new XElement("version", v))));
        versioning.Add(new XElement("lastUpdated", Stamp(lastUpdated)));

        return Document(new XElement(
            "metadata",
            new XElement("groupId", groupId),
            new XElement("artifactId", artifactId),
            versioning));
    }

    /// <summary>
    /// The version level of a snapshot: the newest build and, per classifier and extension, the timestamped version that
    /// resolves it. A release version has no such file in Maven's layout, and gets the plain form.
    /// </summary>
    /// <param name="groupId">The group id.</param>
    /// <param name="artifactId">The artifact id.</param>
    /// <param name="version">The version.</param>
    /// <param name="files">The version's files.</param>
    /// <param name="lastUpdated">When the version last changed.</param>
    public static string ForVersion(string groupId, string artifactId, string version, IEnumerable<MavenFileInfo> files, DateTime lastUpdated)
    {
        var root = new XElement(
            "metadata",
            new XAttribute("modelVersion", "1.1.0"),
            new XElement("groupId", groupId),
            new XElement("artifactId", artifactId),
            new XElement("version", version));

        var builds = files.Where(f => f.SnapshotTimestamp != null).ToList();
        if (MavenNames.IsSnapshot(version) && builds.Count > 0)
        {
            var baseVersion = version.Substring(0, version.Length - MavenNames.SnapshotSuffix.Length);
            var newest = builds.OrderByDescending(f => f.SnapshotTimestamp, StringComparer.Ordinal).ThenByDescending(f => f.SnapshotBuild).First();

            var snapshotVersions = builds
                .GroupBy(f => (f.Classifier, f.Extension))
                .Select(g => g.OrderByDescending(f => f.SnapshotTimestamp, StringComparer.Ordinal).ThenByDescending(f => f.SnapshotBuild).First())
                .OrderBy(f => f.Extension, StringComparer.Ordinal).ThenBy(f => f.Classifier, StringComparer.Ordinal)
                .Select(f =>
                {
                    var element = new XElement("snapshotVersion");
                    if (f.Classifier != null) element.Add(new XElement("classifier", f.Classifier));

                    element.Add(new XElement("extension", f.Extension));
                    element.Add(new XElement("value", $"{baseVersion}-{f.SnapshotTimestamp}-{f.SnapshotBuild.ToString(CultureInfo.InvariantCulture)}"));
                    element.Add(new XElement("updated", Stamp(f.Created)));

                    return element;
                });

            root.Add(new XElement(
                "versioning",
                new XElement(
                    "snapshot",
                    new XElement("timestamp", newest.SnapshotTimestamp),
                    new XElement("buildNumber", newest.SnapshotBuild.ToString(CultureInfo.InvariantCulture))),
                new XElement("lastUpdated", Stamp(lastUpdated)),
                new XElement("snapshotVersions", snapshotVersions)));
        }

        return Document(root);
    }

    /// <summary>Maven's <c>yyyyMMddHHmmss</c> UTC timestamp.</summary>
    public static string Stamp(DateTime value)
        => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

    private static string Document(XElement root)
        => new XDeclaration("1.0", "UTF-8", null) + Environment.NewLine + root.ToString() + Environment.NewLine;
}
