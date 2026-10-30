using System.Collections.Generic;
using Microsoft.AspNetCore.Identity;

namespace BaGet.Identity.Application.Services.Accounts;

/// <summary>What an administrator's deletion of a user did.</summary>
/// <param name="Result">The outcome of deleting the user. Failed with nothing deleted when refused.</param>
/// <param name="KeptWithoutOwners">Sole-owned package IDs left without owners (only admins and the global key manage them), in <see cref="SoleOwnedPackage.Label"/> form.</param>
/// <param name="DeletedPackageIds">Sole-owned package IDs removed with all their versions, in every repository, in <see cref="SoleOwnedPackage.Label"/> form.</param>
/// <param name="Failures">What could not be removed after the user was deleted; these IDs stay ownerless; <see cref="SoleOwnedPackage.Label"/> form.</param>
public record AdminDeletionResult(
    IdentityResult Result,
    IReadOnlyList<string?> KeptWithoutOwners,
    IReadOnlyList<string> DeletedPackageIds,
    IReadOnlyList<string?> Failures);
