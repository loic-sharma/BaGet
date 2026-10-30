namespace BaGet.Identity.Application.UseCases.Account;

/// <summary>What a password-confirmed screen shows about how the caller can confirm.</summary>
/// <param name="HasPassword">The caller has a password to type.</param>
/// <param name="Reauthenticated">For an account without a password: it signed in again with its provider a moment ago.</param>
/// <param name="CanReauthenticate">An external provider is available to sign in again with.</param>
public readonly record struct ConfirmationState(bool HasPassword, bool Reauthenticated, bool CanReauthenticate);
