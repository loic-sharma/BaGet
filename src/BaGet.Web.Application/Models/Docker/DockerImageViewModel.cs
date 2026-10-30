using System;
using System.Collections.Generic;
using BaGet.Core;
using BaGet.Web.Application.Snippets;

namespace BaGet.Web.Application.Models.Docker;

/// <summary>What the page of one Docker image (<c>/docker-images/{image}</c>, <c>?digest=</c> for a manifest) shows.</summary>
public sealed class DockerImageViewModel
{
    /// <summary>The image name from the address (it may contain slashes).</summary>
    public string? Image { get; init; }

    /// <summary>The manifest to show in full (<c>?digest=</c>), or null for the overview.</summary>
    public string? Digest { get; init; }

    /// <summary>Narrows the referrers to one artifact type (<c>?artifactType=</c>); empty keeps them all.</summary>
    public string? ArtifactType { get; init; }

    /// <summary>The HTTP status the page is answered with: 200, or 404 when the image or manifest does not exist (the page then explains).</summary>
    public int StatusCode { get; init; } = 200;

    /// <summary>The image, or null when there is none of that name.</summary>
    public OciImageInfo? Info { get; init; }

    /// <summary>The chosen manifest, or null.</summary>
    public OciManifestDetail? Selected { get; init; }

    /// <summary>The manifests whose <c>subject</c> is the chosen manifest (signatures, SBOMs). Empty without a selection.</summary>
    public IReadOnlyList<OciDescriptor> Referrers { get; init; } = Array.Empty<OciDescriptor>();

    /// <summary>The registry host as a Docker client writes it.</summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>The repository's name.</summary>
    public string RepositoryName { get; init; } = string.Empty;

    /// <summary>Whether the repository caches an upstream registry (it takes no pushes).</summary>
    public bool IsRemote { get; init; }

    /// <summary>Whether the signed-in user holds Delete on the image, so the manage page is offered.</summary>
    public bool CanManage { get; init; }

    public IReadOnlyList<InstallSnippet> PullTabs { get; init; } = Array.Empty<InstallSnippet>();

    /// <summary>The address of the registry API for the image (what a script calls).</summary>
    public string RegistryUrl { get; init; } = string.Empty;
}
