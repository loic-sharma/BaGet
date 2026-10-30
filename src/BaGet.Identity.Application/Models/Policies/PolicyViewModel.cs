using BaGet.Identity.Application.Models.Shared;

namespace BaGet.Identity.Application.Models.Policies;

/// <summary>
/// Shared shape of the policy screens (retention, scanning, replication): the password confirmation partial's inputs and the
/// outcome of the last action, which <c>AdminControllerBase.Flash</c> carries across a redirect.
/// </summary>
public abstract class PolicyViewModel : ConfirmationModel, IFlashModel
{
    /// <inheritdoc />
    public string? Message { get; set; }

    /// <inheritdoc />
    public string? Error { get; set; }
}
