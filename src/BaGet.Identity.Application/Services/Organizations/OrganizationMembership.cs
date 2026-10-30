namespace BaGet.Identity.Application.Services.Organizations;

/// <summary>An organization a user belongs to, as the account deletion sees it: whether the user is its last administrator and how many other members it has.</summary>
internal sealed record OrganizationMembership(int Key, string? Name, bool IsLastAdmin, int OtherMembers);
