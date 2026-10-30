using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace BaGet.Core;

/// <summary>A dependency a POM declares (versions and scopes as written; properties are not resolved).</summary>
/// <param name="GroupId">The group id.</param>
/// <param name="ArtifactId">The artifact id.</param>
/// <param name="Version">The version, or null (managed elsewhere).</param>
/// <param name="Scope">The scope, or null (compile).</param>
/// <param name="Optional">Whether the dependency is optional.</param>
public sealed record MavenPomDependency(string GroupId, string ArtifactId, string? Version, string? Scope, bool Optional);

/// <summary>What the search and the pages read from a POM.</summary>
/// <param name="GroupId">The group id (the parent's when the POM has none).</param>
/// <param name="ArtifactId">The artifact id.</param>
/// <param name="Version">The version (the parent's when the POM has none).</param>
/// <param name="Packaging">The packaging, <c>jar</c> by default.</param>
/// <param name="Name">The <c>name</c>.</param>
/// <param name="Description">The <c>description</c>.</param>
/// <param name="Url">The project <c>url</c>.</param>
/// <param name="Parent">The parent as <c>group:artifact:version</c>, or null.</param>
/// <param name="Dependencies">The declared dependencies, in order.</param>
public sealed record MavenPomInfo(
    string? GroupId,
    string? ArtifactId,
    string? Version,
    string Packaging,
    string? Name,
    string? Description,
    string? Url,
    string? Parent,
    IReadOnlyList<MavenPomDependency> Dependencies);

/// <summary>Reads the few facts of a POM that the repository shows and searches.</summary>
public static class MavenPom
{
    /// <summary>A POM larger than this is stored but not read (a real one is a few kilobytes).</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Reads <paramref name="pom"/>. DTDs are refused (no entity expansion), and the POM's namespace is ignored.
    /// </summary>
    /// <returns>Null when the bytes are not an XML document with a <c>project</c> root.</returns>
    public static MavenPomInfo? TryParse(Stream pom)
    {
        if (pom == null) throw new ArgumentNullException(nameof(pom));

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxBytes,
                IgnoreComments = true,
            };

            using var reader = XmlReader.Create(pom, settings);
            var root = XDocument.Load(reader).Root;
            if (root == null || root.Name.LocalName != "project") return null;

            var parent = root.Elements().FirstOrDefault(e => e.Name.LocalName == "parent");
            var parentGroup = Text(parent, "groupId");
            var parentVersion = Text(parent, "version");

            var dependencies = root.Elements().Where(e => e.Name.LocalName == "dependencies")
                .SelectMany(d => d.Elements().Where(e => e.Name.LocalName == "dependency"))
                .Select(d => new MavenPomDependency(
                    Text(d, "groupId") ?? string.Empty,
                    Text(d, "artifactId") ?? string.Empty,
                    Text(d, "version"),
                    Text(d, "scope"),
                    string.Equals(Text(d, "optional"), "true", StringComparison.OrdinalIgnoreCase)))
                .Where(d => d.GroupId.Length > 0 && d.ArtifactId.Length > 0)
                .Take(500)
                .ToList();

            return new MavenPomInfo(
                Text(root, "groupId") ?? parentGroup,
                Text(root, "artifactId"),
                Text(root, "version") ?? parentVersion,
                Text(root, "packaging") ?? "jar",
                Text(root, "name"),
                Text(root, "description"),
                Text(root, "url"),
                parent == null ? null : $"{parentGroup}:{Text(parent, "artifactId")}:{parentVersion}",
                dependencies);
        }
        catch (Exception e) when (e is XmlException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? Text(XElement? parent, string name)
    {
        var value = parent?.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
        value = value == null ? null : string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// A POM for a deploy made with only coordinates (the browser upload page): the minimum Maven needs to resolve the
    /// artifact.
    /// </summary>
    public static string Generate(string groupId, string artifactId, string version, string packaging, string? name = null, string? description = null)
    {
        XNamespace ns = "http://maven.apache.org/POM/4.0.0";
        var project = new XElement(
            ns + "project",
            new XElement(ns + "modelVersion", "4.0.0"),
            new XElement(ns + "groupId", groupId),
            new XElement(ns + "artifactId", artifactId),
            new XElement(ns + "version", version),
            new XElement(ns + "packaging", packaging));
        if (!string.IsNullOrWhiteSpace(name)) project.Add(new XElement(ns + "name", name));
        if (!string.IsNullOrWhiteSpace(description)) project.Add(new XElement(ns + "description", description));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), project).Declaration + Environment.NewLine + project.ToString() + Environment.NewLine;
    }
}
