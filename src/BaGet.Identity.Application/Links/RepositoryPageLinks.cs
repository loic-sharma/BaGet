using System;
using System.Linq;
using BaGet.Core;

namespace BaGet.Identity.Application.Links;

/// <summary>
/// App-relative (<c>~/</c>) addresses of a package's pages in a given repository. The one place that decides
/// where a package page of a repository lives, so that pages that list packages of several repositories (the
/// account blockers, the administrator lists) link to the right one and a change of page routing is a change here.
/// </summary>
/// <remarks>
/// The default repository's pages are at the root; any other repository's are under <c>r/{name}/</c>, like its
/// protocol routes. Pass the result through <c>Url.Content</c> to honour the path base.
/// </remarks>
public static class RepositoryPageLinks
{
    /// <summary>The package page of <paramref name="id"/>.</summary>
    /// <param name="repositoryName">The repository; null or the default's name for the root pages.</param>
    /// <param name="id">The package ID.</param>
    /// <param name="format">The repository's format: an npm package has its own pages (<c>npm-packages/{name}</c>), a Maven artifact too (<c>maven-artifacts/{group}/{artifact}</c>).</param>
    public static string Package(string? repositoryName, string? id, RepositoryFormat format = RepositoryFormat.NuGet)
        => format == RepositoryFormat.Npm
            ? Prefix(repositoryName) + "npm-packages/" + NpmPath(id)
            : format == RepositoryFormat.Maven
            ? Prefix(repositoryName) + "maven-artifacts/" + MavenPath(id)
            : format == RepositoryFormat.Docker
            ? Prefix(repositoryName) + "docker-images/" + NpmPath(id)
            : Prefix(repositoryName) + "packages/" + Uri.EscapeDataString(id ?? string.Empty);

    /// <summary>The management page of <paramref name="id"/>.</summary>
    /// <param name="repositoryName">The repository; null or the default's name for the root pages.</param>
    /// <param name="id">The package ID.</param>
    /// <param name="format">The repository's format.</param>
    public static string Manage(string? repositoryName, string? id, RepositoryFormat format = RepositoryFormat.NuGet)
        => format == RepositoryFormat.Npm
            ? Prefix(repositoryName) + "npm-manage/" + NpmPath(id)
            : format == RepositoryFormat.Maven
            ? Prefix(repositoryName) + "maven-manage/" + MavenPath(id)
            : format == RepositoryFormat.Docker
            ? Prefix(repositoryName) + "docker-manage/" + NpmPath(id)
            : Package(repositoryName, id) + "/manage";

    // A scoped name keeps its slash in the address (the pages take the rest of the path).
    private static string NpmPath(string? id)
        => string.Join("/", (id ?? string.Empty).Split('/').Select(Uri.EscapeDataString));

    // A Maven "group:artifact" is two path segments (group, artifact) in the address.
    private static string MavenPath(string? id)
        => string.Join("/", (id ?? string.Empty).Split(':').Select(Uri.EscapeDataString));

    private static string Prefix(string? repositoryName)
        => string.IsNullOrEmpty(repositoryName) || repositoryName == Repository.DefaultName
            ? "~/"
            : "~/r/" + Uri.EscapeDataString(repositoryName) + "/";
}
