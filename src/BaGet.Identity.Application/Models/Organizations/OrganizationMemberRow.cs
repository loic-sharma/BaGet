namespace BaGet.Identity.Application.Models.Organizations;

/// <summary>A member as the organization page lists them.</summary>
/// <param name="UserName">The user name.</param>
/// <param name="IsAdmin">Whether the member administers the organization.</param>
public record OrganizationMemberRow(string? UserName, bool IsAdmin);
