using System;
using System.Collections.Generic;
using System.Linq;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Packages;

/// <summary>What the ownerless packages screen (<c>/admin/packages</c>) shows: the package IDs nobody owns, in every repository.</summary>
public sealed class AdminPackagesViewModel
{
    /// <summary>How many IDs a page lists.</summary>
    public const int PageSize = 50;

    /// <summary>One-based page number (the <c>p</c> query value).</summary>
    public int PageNumber { get; set; } = 1;

    /// <summary>The ownerless IDs on this page.</summary>
    public IReadOnlyList<AdminPackageRow> Packages { get; internal set; } = Array.Empty<AdminPackageRow>();

    /// <summary>Whether a Repository column is worth showing: some listed ID is outside the default repository.</summary>
    public bool ShowRepository => Packages.Any(r => r.RepositoryName != Repository.DefaultName);

    /// <summary>The number of pages.</summary>
    public int PageCount { get; internal set; } = 1;
}
