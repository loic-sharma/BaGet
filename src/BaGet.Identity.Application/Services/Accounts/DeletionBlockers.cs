using System;
using System.Collections.Generic;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Identity.Application.Services.Accounts;

/// <summary>
/// What stops a user from deleting their account.
/// </summary>
/// <param name="LastOwnerPackageIds">
/// Package IDs the user is the only direct owner of, in any repository (ownership is per repository, so the same
/// ID in two repositories counts once each). Same rule as
/// <see cref="PackageOwnershipService.RemoveOwnerAsync"/>: an owned ID keeps an owner.
/// </param>
/// <param name="Organizations">
/// Organizations the user is the last administrator of while others still belong to it, or
/// that own packages and so cannot be left without anyone to run them. Same rule as
/// <see cref="PackageOwnershipService.RemoveMemberAsync"/>.
/// </param>
/// <param name="LastOwnerPackages">
/// The same IDs per repository (the same ID sole-owned in two repositories is two entries), with the repository's
/// name so a page can name it and link to that repository's Manage page.
/// </param>
/// <param name="LastAdministrator">
/// The user is the server's only administrator, so deleting them would leave nobody able to
/// change the settings.
/// </param>
public record DeletionBlockers(
    IReadOnlyList<string?> LastOwnerPackageIds,
    IReadOnlyList<string?> Organizations,
    bool LastAdministrator = false,
    IReadOnlyList<SoleOwnedPackage>? LastOwnerPackages = null)
{
    /// <summary>Per-repository detail of <see cref="LastOwnerPackageIds"/>; never null.</summary>
    public IReadOnlyList<SoleOwnedPackage> LastOwnerPackages { get; init; } = LastOwnerPackages ?? Array.Empty<SoleOwnedPackage>();

    /// <summary>Whether anything blocks the deletion.</summary>
    public bool Any => LastOwnerPackageIds.Count > 0 || Organizations.Count > 0 || LastAdministrator;
}
