using BaGet.Identity.Application.Models.Shared;

namespace BaGet.Identity.Application.Models.Access;

/// <summary>
/// What every access-control screen (groups, permission targets, projects) shows besides its own data: the outcome of the last
/// action, why it was refused, whether the signed-in user is a server administrator (rather than only a project administrator)
/// and what the password confirmation partial needs.
/// </summary>
public abstract class AccessViewModel : ConfirmationModel, IFlashModel
{
    /// <inheritdoc />
    public string? Message { get; set; }

    /// <inheritdoc />
    public string? Error { get; set; }

    /// <summary>Whether the signed-in user is a server administrator.</summary>
    public bool IsServerAdministrator { get; internal set; }
}
