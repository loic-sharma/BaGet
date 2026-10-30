namespace BaGet.Identity.Application.Services.Organizations;

/// <summary>The outcome of an administrator's change to an organization.</summary>
public enum OrganizationChange
{
    /// <summary>Done.</summary>
    Success,

    /// <summary>No organization (or no such member) with that name.</summary>
    NotFound,

    /// <summary>The new name is empty, too long or has characters that cannot be in a URL path segment.</summary>
    InvalidName,

    /// <summary>The new name is another user's or organization's: they share one namespace.</summary>
    NameTaken,

    /// <summary>The organization owns package IDs (in some repository), so it cannot be deleted.</summary>
    OwnsPackages,

    /// <summary>An organization must keep at least one administrator.</summary>
    LastAdministrator,
}
