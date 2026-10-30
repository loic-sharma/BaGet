namespace BaGet.Identity.Application.Models.Shared;

/// <summary>What the password confirmation partial needs.</summary>
/// <param name="Id">The input's id.</param>
/// <param name="HasPassword">Whether the administrator has a password to type.</param>
/// <param name="Reauthenticated">For an account without a password: it signed in again a moment ago.</param>
/// <param name="CanReauthenticate">Whether a provider is available to sign in again with.</param>
/// <param name="ReturnPage">The page to come back to after signing in again.</param>
public record ConfirmPasswordView(string Id, bool HasPassword, bool Reauthenticated, bool CanReauthenticate, string ReturnPage);
