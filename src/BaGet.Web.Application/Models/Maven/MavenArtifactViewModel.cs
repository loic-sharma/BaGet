using System;
using System.Collections.Generic;
using BaGet.Core;
using BaGet.Web.Application.Snippets;

namespace BaGet.Web.Application.Models.Maven;

/// <summary>What the page of one Maven artifact (<c>/maven-artifacts/{group}/{artifact}/{version?}</c>) shows.</summary>
public sealed class MavenArtifactViewModel
{
    /// <summary>The group id from the address.</summary>
    public string? Group { get; init; }

    /// <summary>The artifact id from the address.</summary>
    public string? Artifact { get; init; }

    /// <summary>The version from the address; the latest when absent.</summary>
    public string? Version { get; init; }

    /// <summary>The HTTP status the page is answered with: 200, or 404 when the artifact or version does not exist (the page then explains).</summary>
    public int StatusCode { get; init; } = 200;

    /// <summary>The artifact, or null when there is none of those coordinates.</summary>
    public MavenArtifactInfo? Info { get; init; }

    /// <summary>The version the page shows, with its files and POM.</summary>
    public MavenVersionDetail? Selected { get; init; }

    /// <summary>The address Maven is configured with for this repository.</summary>
    public string RepositoryUrl { get; init; } = string.Empty;

    /// <summary>The repository's name (the <c>id</c> in <c>settings.xml</c>).</summary>
    public string RepositoryName { get; init; } = string.Empty;

    public IReadOnlyList<InstallSnippet> InstallTabs { get; init; } = Array.Empty<InstallSnippet>();

    /// <summary>The owners, when accounts are on; empty otherwise.</summary>
    public IReadOnlyList<string> Owners { get; init; } = Array.Empty<string>();

    /// <summary>Whether the signed-in user can open the manage page (an owner or an administrator).</summary>
    public bool CanManage { get; init; }

    /// <summary>The address of a file of the selected version.</summary>
    public string FileUrl(string fileName)
        => RepositoryUrl + MavenNames.ClientPath(Info!.GroupId, Info.ArtifactId, Selected!.Summary.Version, fileName);
}
