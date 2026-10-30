namespace BaGet.Identity.Application.UseCases.Admin;

/// <summary>What the "add a member" form posts.</summary>
public sealed class AddOrganizationMemberForm
{
    /// <summary>The organization.</summary>
    public string? Organization { get; set; }

    /// <summary>The user to add.</summary>
    public string? UserName { get; set; }

    /// <summary>Whether the new member administers the organization.</summary>
    public bool IsAdmin { get; set; }
}
