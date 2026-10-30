using BaGet.Identity.Application.Screens;
using System;
using System.Collections.Generic;
using BaGet.Core;
using BaGet.Identity.Application.Models.Shared;

namespace BaGet.Identity.Application.Models.Repositories;

/// <summary>
/// What the repository screen (<c>/admin/repositories/{name}</c>) shows: the repository, its access summary and, by kind, its
/// upstream and cache (remote), members and preview (virtual) or Maven settings. The posted fields (anonymous read, upstream, preview
/// and purge inputs) live here too, so a refused action shows again what was typed.
/// </summary>
public sealed class RepositoryEditViewModel : ConfirmationModel, IFlashMessage
{
    /// <summary>The repository's name (route).</summary>
    public string? Name { get; set; }

    /// <summary>Whether requests without credentials may read the repository.</summary>
    public bool AnonymousRead { get; set; }

    /// <summary>The repository.</summary>
    public RepositorySummary? Repository { get; internal set; }

    /// <summary>The upstream fields of a remote repository.</summary>
    public RemoteFormModel Remote { get; set; } = new();

    /// <summary>The remote repository's upstream, null for the other kinds.</summary>
    public RemoteRepositoryDetail? RemoteDetail { get; internal set; }

    /// <summary>The settings of a local Maven repository, null for the other repositories.</summary>
    public MavenRepositorySettings? MavenSettings { get; internal set; }

    /// <summary>What a remote repository stores and remembers.</summary>
    public RemoteCacheOverview? Cache { get; internal set; }

    /// <summary>The virtual repository's members and deploy repository, null for the other kinds.</summary>
    public VirtualRepositoryDetail? VirtualDetail { get; internal set; }

    /// <summary>The repositories that can still be added to a virtual repository.</summary>
    public IReadOnlyList<RepositoryInfo> MemberCandidates { get; internal set; } = Array.Empty<RepositoryInfo>();

    /// <summary>The virtual repositories that list this repository as a member.</summary>
    public IReadOnlyList<string> ReferencedBy { get; internal set; } = Array.Empty<string>();

    /// <summary>The package id to resolve in the preview.</summary>
    public string? PreviewPackageId { get; set; }

    /// <summary>The package version to resolve in the preview.</summary>
    public string? PreviewVersion { get; set; }

    /// <summary>Where the previewed package version would come from.</summary>
    public ResolutionPreview? Preview { get; internal set; }

    /// <summary>The outcome of "Test connection".</summary>
    public RemoteConnectionResult? TestResult { get; internal set; }

    /// <inheritdoc />
    public string? Message { get; set; }

    /// <summary>Why the last action was refused.</summary>
    public string? Error { get; internal set; }

    /// <summary>The package id to purge from the cache.</summary>
    public string? PackageId { get; set; }

    /// <summary>The version to purge (blank = every version).</summary>
    public string? PackageVersion { get; set; }

    /// <summary>Who effectively holds each action in the repository; null for the default repository when the database has no access model.</summary>
    public RepositoryAccessSummary? Access { get; internal set; }

    /// <summary>The screen of the repository's format that lists its packages (<c>Home/Index</c> for NuGet, <c>NpmBrowse/Packages</c> for npm).</summary>
    public ScreenRef BrowseScreen { get; internal set; } = ScreenCatalog.Index;

    /// <summary>The repository's feed address.</summary>
    public string FeedUrl { get; internal set; } = string.Empty;

    /// <summary>For the default repository: the remote repository it fetches through, when <c>Mirror:Enabled</c>.</summary>
    public RemoteRepositoryDetail? MirrorRepository { get; internal set; }

    /// <summary>Whether <c>Mirror:Enabled</c> makes the default repository fetch through <see cref="MirrorRepository"/>.</summary>
    public bool MirrorActive { get; internal set; }

    /// <summary>The name of what a package is called in this repository's format (shown in the labels).</summary>
    public string PackageWord => Repository?.Format switch { RepositoryFormat.Npm => "package name", RepositoryFormat.Maven => "artifact (group:artifact)", RepositoryFormat.Docker => "image name", _ => "package id" };

    /// <summary>What the "version" of a package is called in this repository's format (a Docker image has a tag or a digest).</summary>
    public string VersionWord => Repository?.Format == RepositoryFormat.Docker ? "Tag or digest" : "Version";

    /// <summary>The label of the field that names a package (a Docker image is an image name).</summary>
    public string PackageLabel => Repository?.Format switch { RepositoryFormat.Npm => "Package name", RepositoryFormat.Maven => "Artifact (group:artifact)", RepositoryFormat.Docker => "Image name", _ => "Package id" };
}
