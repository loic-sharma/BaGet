using System;
using System.Collections.Generic;
using BaGet.Core;
using BaGet.Web.Application.Snippets;
using BaGet.Web.Application.UseCases;

namespace BaGet.Web.Application.Models.Maven;

/// <summary>What the Maven front page (<c>/maven-artifacts</c>) shows: this page of search results and the client configuration.</summary>
public sealed class MavenArtifactsViewModel
{
    /// <summary>The search text.</summary>
    public string? Query { get; init; }

    /// <summary>The page, from 1.</summary>
    public int PageIndex { get; init; } = 1;

    /// <summary>This page of results.</summary>
    public MavenSearchPage Result { get; init; } = new(0, Array.Empty<MavenSearchHit>());

    /// <summary>The repository's policy, redeploy and snapshot retention.</summary>
    public MavenRepositorySettings Settings { get; init; } = new();

    /// <summary>The address Maven is configured with for this repository.</summary>
    public string RepositoryUrl { get; init; } = string.Empty;

    /// <summary>The kind of the repository: a remote one lists what it cached, a virtual one what its members hold.</summary>
    public RepositoryKind Kind { get; init; }

    /// <summary>The repository's name, which is the <c>id</c> in <c>settings.xml</c> and the POM.</summary>
    public string RepositoryName { get; init; } = string.Empty;

    /// <summary>Whether another page follows.</summary>
    public bool HasNext => PageIndex * MavenBrowseUseCase.ResultsPerPage < Result.Total;

    public IReadOnlyList<InstallSnippet> ConfigurationTabs { get; init; } = Array.Empty<InstallSnippet>();
}
