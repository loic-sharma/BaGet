using System.Collections.Generic;
using BaGet.Identity.Application.Services.Organizations;

namespace BaGet.Identity.Application.Services.Accounts;

/// <summary>What deleting a user as an administrator would remove or change.</summary>
/// <param name="ApiKeys">How many API keys go.</param>
/// <param name="DirectPackageIds">Package IDs the user owns directly.</param>
/// <param name="SoleOwnedPackages">The subset nobody else owns, in every repository.</param>
/// <param name="Organizations">What happens to each organization the user belongs to.</param>
public record AdminDeletionPreview(
    int ApiKeys,
    IReadOnlyList<string?> DirectPackageIds,
    IReadOnlyList<SoleOwnedPackage> SoleOwnedPackages,
    IReadOnlyList<OrganizationOutcome> Organizations);
