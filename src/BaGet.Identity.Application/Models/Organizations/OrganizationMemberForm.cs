namespace BaGet.Identity.Application.Models.Organizations;

/// <summary>Adding a member to an organization, changing their administrator flag or removing them.</summary>
/// <param name="UserName">The member's user name.</param>
/// <param name="IsAdmin">Whether the member administers the organization (ignored when removing).</param>
/// <param name="Password">The administrator's password.</param>
public sealed record OrganizationMemberForm(string? UserName, bool IsAdmin, string? Password);
