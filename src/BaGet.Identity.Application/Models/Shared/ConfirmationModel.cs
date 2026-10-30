namespace BaGet.Identity.Application.Models.Shared;

/// <summary>
/// What a screen that asks the administrator's password again needs to render the confirmation partial: whether there is a
/// password to type, or else a recent provider sign-in. Shared base of the view models of the admin screens.
/// </summary>
public abstract class ConfirmationModel
{
    /// <summary>Whether the signed in administrator has a password to confirm with.</summary>
    public bool HasPassword { get; internal set; } = true;

    /// <summary>For an account without a password: it signed in again with its provider a moment ago.</summary>
    public bool Reauthenticated { get; internal set; }

    /// <summary>Whether an external provider is available to sign in again with.</summary>
    public bool CanReauthenticate { get; internal set; }

    /// <summary>The page to come back to after signing in again (the model of the confirmation partial).</summary>
    public string ReturnPage { get; internal set; } = string.Empty;

    /// <summary>The model of the password confirmation partial, with <paramref name="id"/> for the input.</summary>
    public ConfirmPasswordView Confirmation(string id)
        => new(id, HasPassword, Reauthenticated, CanReauthenticate, ReturnPage);
}
