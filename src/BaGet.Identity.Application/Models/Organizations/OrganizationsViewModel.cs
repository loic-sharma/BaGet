using System;
using System.Collections.Generic;

namespace BaGet.Identity.Application.Models.Organizations;

/// <summary>The signed-in user's organizations screen.</summary>
public sealed class OrganizationsViewModel
{
    /// <summary>The organizations the user belongs to.</summary>
    public IReadOnlyList<AccountOrganization> Organizations { get; set; } = Array.Empty<AccountOrganization>();

    /// <summary>The answer to the last action.</summary>
    public string? Message { get; set; }
}
