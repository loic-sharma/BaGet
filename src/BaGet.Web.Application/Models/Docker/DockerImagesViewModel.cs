using System;
using System.Collections.Generic;
using BaGet.Core;
using BaGet.Web.Application.Snippets;
using BaGet.Web.Application.UseCases;

namespace BaGet.Web.Application.Models.Docker;

/// <summary>What the Docker front page (<c>/docker-images</c>) shows: this page of images and the commands for the client.</summary>
public sealed class DockerImagesViewModel
{
    /// <summary>The search text.</summary>
    public string? Query { get; init; }

    /// <summary>The page, from 1.</summary>
    public int PageIndex { get; init; } = 1;

    /// <summary>This page of results.</summary>
    public OciImagePage Result { get; init; } = new(0, Array.Empty<OciImageSummary>());

    /// <summary>The registry host as a Docker client writes it (<c>host:port</c>).</summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>Whether the registry is reached over plain http (the Docker daemon then needs it as an insecure registry).</summary>
    public bool IsHttp { get; init; }

    /// <summary>The repository's name, the first path segment of every image reference.</summary>
    public string RepositoryName { get; init; } = string.Empty;

    /// <summary>Whether the repository caches an upstream registry (it takes no pushes).</summary>
    public bool IsRemote { get; init; }

    /// <summary>Whether the repository merges other repositories (a push goes to its deploy repository).</summary>
    public bool IsVirtual { get; init; }

    /// <summary>Whether another page follows.</summary>
    public bool HasNext => PageIndex * DockerBrowseUseCase.ResultsPerPage < Result.Total;

    public IReadOnlyList<InstallSnippet> CommandTabs { get; init; } = Array.Empty<InstallSnippet>();
}
