namespace BaGet.Identity.Application.Models.Organizations;

/// <summary>One member of an organization (a flat record, so the screen's JSON names its fields).</summary>
/// <param name="UserName">The member's user name.</param>
/// <param name="IsAdmin">Whether the member administers the organization.</param>
public sealed record AccountOrganizationMember(string UserName, bool IsAdmin);
