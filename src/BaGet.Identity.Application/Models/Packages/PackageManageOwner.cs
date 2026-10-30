namespace BaGet.Identity.Application.Models.Packages;

/// <summary>An owner of a package ID.</summary>
/// <param name="OwnerName">The user or organization name.</param>
/// <param name="IsVerified">Whether the ID is reserved for this owner.</param>
public sealed record PackageManageOwner(string? OwnerName, bool IsVerified);
