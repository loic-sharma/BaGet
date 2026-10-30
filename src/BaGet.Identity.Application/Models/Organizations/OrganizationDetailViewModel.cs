using System;
using System.Collections.Generic;
using BaGet.Core;
using BaGet.Identity.Application.Models.Shared;

namespace BaGet.Identity.Application.Models.Organizations;

/// <summary>What the organization screen (<c>/admin/organizations/{name}</c>) shows: the organization, its members and the last action.</summary>
public sealed class OrganizationDetailViewModel : ConfirmationModel, IFlashModel
{
    /// <summary>The organization's name (route).</summary>
    public string? Name { get; set; }

    /// <summary>The organization.</summary>
    public Organization? Organization { get; internal set; }

    /// <summary>Its members, by name.</summary>
    public IReadOnlyList<OrganizationMemberRow> Members { get; internal set; } = Array.Empty<OrganizationMemberRow>();

    /// <summary>How many package IDs it owns, in every repository.</summary>
    public int OwnedPackageIds { get; internal set; }

    /// <inheritdoc />
    public string? Message { get; set; }

    /// <inheritdoc />
    public string? Error { get; set; }
}
