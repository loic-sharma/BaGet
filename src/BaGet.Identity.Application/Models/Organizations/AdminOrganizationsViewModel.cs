using System;
using System.Collections.Generic;
using BaGet.Identity.Application.Services.Organizations;

namespace BaGet.Identity.Application.Models.Organizations;

/// <summary>What the organizations screen (<c>/admin/organizations</c>) lists: every organization with its members and packages.</summary>
public sealed class AdminOrganizationsViewModel
{
    /// <summary>The organizations, by name.</summary>
    public IReadOnlyList<OrganizationSummary> Organizations { get; internal set; } = Array.Empty<OrganizationSummary>();
}
