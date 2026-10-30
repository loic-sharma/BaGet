using System;
using System.Collections.Generic;
using BaGet.Core;
using BaGet.Protocol.Models;

namespace BaGet.Web.Application.Screens;

/// <summary>What the front page and the package list show: the search that was asked for and its results.</summary>
public sealed record IndexScreen
{
    /// <summary>The search text.</summary>
    public string? Query { get; init; }

    /// <summary>The 1-based page of results.</summary>
    public int PageIndex { get; init; } = 1;

    /// <summary>The package type filter, <c>any</c> for none.</summary>
    public string PackageType { get; init; } = "any";

    /// <summary>The target framework filter, <c>any</c> for none.</summary>
    public string Framework { get; init; } = "any";

    /// <summary>Whether prerelease versions are included.</summary>
    public bool Prerelease { get; init; } = true;

    /// <summary>The requested order.</summary>
    public SearchSort SortBy { get; init; } = SearchSort.Relevance;

    /// <summary>The orders the search provider can apply on the server; with only the default there is nothing to choose.</summary>
    public IReadOnlyList<SearchSort> SupportedSorts { get; init; } = new[] { SearchSort.Relevance };

    /// <summary>True for the front page: the repository's root with no search. Everything else lists packages.</summary>
    public bool IsHome { get; init; }

    /// <summary>The feed's totals, shown on the front page; null when the database cannot report them.</summary>
    public FeedStatistics? Totals { get; init; }

    /// <summary>The page of results (empty on the front page).</summary>
    public IReadOnlyList<SearchResult> Packages { get; init; } = Array.Empty<SearchResult>();

    /// <summary>When each listed package ID was last published, by ID; empty when unsupported.</summary>
    public IReadOnlyDictionary<string, DateTime> LastUpdated { get; init; } = new Dictionary<string, DateTime>();
}
