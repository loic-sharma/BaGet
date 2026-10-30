using System;
using System.Collections.Generic;
using BaGet.Identity.Application.Models.Repositories;

namespace BaGet.Identity.Application.Models.Account;

/// <summary>The "My access" screen: the signed-in user's own effective permissions.</summary>
public sealed class AccountAccessViewModel
{
    /// <summary>A package ID to evaluate the grants for; empty means the repository as a whole.</summary>
    public string? Package { get; set; }

    /// <summary>The repositories where the user holds some action.</summary>
    public IReadOnlyList<RepositoryAccessRow> Rows { get; set; } = Array.Empty<RepositoryAccessRow>();

    /// <summary>The projects the user administers.</summary>
    public IReadOnlyList<string> AdministeredProjects { get; set; } = Array.Empty<string>();

    /// <summary>Whether the configured database has an access model at all.</summary>
    public bool ModelAvailable { get; set; } = true;
}
