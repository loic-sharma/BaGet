namespace BaGet.Identity.Application.UseCases.Admin;

/// <summary>What the "remove a member" (or "leave") form posts.</summary>
public sealed class RemoveOrganizationMemberForm
{
    /// <summary>The organization.</summary>
    public string? Organization { get; set; }

    /// <summary>The member to remove.</summary>
    public string? UserName { get; set; }
}
