using System;
using System.Collections.Generic;

namespace BaGet.Identity.Application.Models.Packages;

/// <summary>What the user packages screen (<c>/admin/users/{name}/packages</c>) shows: the package IDs one user owns.</summary>
public sealed class UserPackagesViewModel
{
    /// <summary>The user name, from the route.</summary>
    public string? Name { get; set; }

    /// <summary>The user's packages.</summary>
    public IReadOnlyList<AdminPackageRow> Packages { get; internal set; } = Array.Empty<AdminPackageRow>();
}
