namespace BaGet.Identity.Application.Models.Organizations;

/// <summary>Renaming an organization.</summary>
/// <param name="NewName">The new name.</param>
/// <param name="Password">The administrator's password.</param>
public sealed record OrganizationRenameForm(string? NewName, string? Password);
