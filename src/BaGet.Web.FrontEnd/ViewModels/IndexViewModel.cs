using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BaGet.Core;
using BaGet.Protocol.Models;
using Microsoft.AspNetCore.Mvc;
using BaGet.Web.Application.Models.Browse;
using BaGet.Web.Application.Screens;

namespace BaGet.Web.V3.ViewModels;

/// <summary>
/// What the front page and the package list show: the search the visitor asked for (bound from the query string, same names
/// as the former Razor Page's properties) and the results the use case found (filled from the screen the JSON twin answered).
/// </summary>
public sealed class IndexViewModel
{
    /// <summary>The search text (<c>q</c>).</summary>
    [FromQuery(Name = "q")]
    public string? Query { get; set; }

    /// <summary>The 1-based page of results (<c>p</c>).</summary>
    [FromQuery(Name = "p")]
    [Range(1, int.MaxValue)]
    public int PageIndex { get; set; } = 1;

    /// <summary>The package type filter, <c>any</c> for none.</summary>
    [FromQuery]
    public string PackageType { get; set; } = "any";

    /// <summary>The target framework filter, <c>any</c> for none.</summary>
    [FromQuery]
    public string Framework { get; set; } = "any";

    /// <summary>Whether prerelease versions are included.</summary>
    [FromQuery]
    public bool Prerelease { get; set; } = true;

    /// <summary>The requested order (<c>sortBy</c>).</summary>
    [FromQuery(Name = "sortBy")]
    public SearchSort SortBy { get; set; } = SearchSort.Relevance;

    /// <summary>The orders the search provider can apply on the server; with only the default the page shows no sort control.</summary>
    public IReadOnlyCollection<SearchSort> SupportedSorts { get; internal set; } = new[] { SearchSort.Relevance };

    /// <summary>Whether there is anything to choose among <see cref="SupportedSorts"/>.</summary>
    public bool CanSort => SupportedSorts.Count > 1;

    /// <summary>The page of results.</summary>
    public IReadOnlyList<SearchResult>? Packages { get; internal set; }

    /// <summary>True for the site's front page: the root URL with no search. Everything else lists packages.</summary>
    public bool IsHome { get; internal set; }

    /// <summary>The feed's totals, shown on the front page; null when the database cannot report them.</summary>
    public FeedStatistics? Totals { get; internal set; }

    /// <summary>When each listed package ID was last published, by ID; empty when unsupported.</summary>
    public IReadOnlyDictionary<string, DateTime> LastUpdated { get; internal set; } = new Dictionary<string, DateTime>();

    /// <summary>The query of the request, as the use case takes it.</summary>
    internal IndexQuery ToQuery() => new(Query, PageIndex, PackageType, Framework, Prerelease, SortBy);

    /// <summary>Fills this model (which carries the bound query) from what the JSON twin answered.</summary>
    internal IndexViewModel Fill(IndexScreen screen)
    {
        SupportedSorts = screen.SupportedSorts;
        IsHome = screen.IsHome;
        Totals = screen.Totals;
        Packages = screen.Packages;
        LastUpdated = screen.LastUpdated;
        return this;
    }
}
