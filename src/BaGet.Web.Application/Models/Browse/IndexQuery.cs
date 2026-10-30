using BaGet.Core;

namespace BaGet.Web.Application.Models.Browse;

/// <summary>What the visitor asked the front page or the package list for (the query string of <c>/</c>, <c>/packages</c>).</summary>
/// <param name="Query">The search text (<c>q</c>).</param>
/// <param name="PageIndex">The 1-based page of results (<c>p</c>).</param>
/// <param name="PackageType">The package type filter, <c>any</c> for none.</param>
/// <param name="Framework">The target framework filter, <c>any</c> for none.</param>
/// <param name="Prerelease">Whether prerelease versions are included.</param>
/// <param name="SortBy">The requested order.</param>
public sealed record IndexQuery(
    string? Query = null,
    int PageIndex = 1,
    string PackageType = "any",
    string Framework = "any",
    bool Prerelease = true,
    SearchSort SortBy = SearchSort.Relevance);
