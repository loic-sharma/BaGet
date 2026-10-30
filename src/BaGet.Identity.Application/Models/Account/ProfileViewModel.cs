using System;
using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Account;

/// <summary>A user's or an organization's public page: the packages it owns.</summary>
public sealed class ProfileViewModel
{
    /// <summary>The account's name.</summary>
    public string? Name { get; set; }

    /// <summary>Whether the account is an organization.</summary>
    public bool IsOrganization { get; set; }

    /// <summary>The owned packages with their downloads.</summary>
    public IReadOnlyList<PackageDownloads> Packages { get; set; } = Array.Empty<PackageDownloads>();

    /// <summary>The members of an organization.</summary>
    public IReadOnlyList<string?> Members { get; set; } = Array.Empty<string>();

    /// <summary>Whether the profile is the signed-in user's own.</summary>
    public bool IsCurrentUser { get; set; }

    /// <summary>The account's avatar address, or null when Gravatar is off.</summary>
    public string? AvatarUrl { get; set; }
}
