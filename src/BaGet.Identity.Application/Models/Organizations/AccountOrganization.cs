using System.Collections.Generic;

namespace BaGet.Identity.Application.Models.Organizations;

/// <summary>One organization with its members.</summary>
public class AccountOrganization
{
    /// <summary>The organization's name.</summary>
    public string? Name { get; set; }

    /// <summary>Whether the current user administers it.</summary>
    public bool IsAdmin { get; set; }

    /// <summary>The members, by user name.</summary>
    public IReadOnlyList<AccountOrganizationMember>? Members { get; set; }
}
