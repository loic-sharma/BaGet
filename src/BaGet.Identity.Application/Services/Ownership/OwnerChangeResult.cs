namespace BaGet.Identity.Application.Services.Ownership;

public enum OwnerChangeResult
{
    Success,

    /// <summary>
    /// No user or organization has that name.
    /// </summary>
    UnknownAccount,

    AlreadyOwner,

    NotOwner,

    /// <summary>
    /// A package ID with owners must keep at least one.
    /// </summary>
    LastOwner,
}
