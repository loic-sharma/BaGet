using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Identity.Application.Models.Settings;

/// <summary>What the account settings screen shows (change email, change password, delete the account).</summary>
public sealed class SettingsViewModel
{
    /// <summary>The account's email address.</summary>
    public string? Email { get; set; }

    /// <summary>Whether the address is confirmed.</summary>
    public bool EmailConfirmed { get; set; }

    /// <summary>Whether the account has a password (else it signs in through a provider).</summary>
    public bool HasPassword { get; set; } = true;

    /// <summary>For an account without a password: it signed in again with its provider a moment ago.</summary>
    public bool Reauthenticated { get; set; }

    /// <summary>Whether an external provider is configured to sign in again with.</summary>
    public bool CanReauthenticate { get; set; }

    /// <summary>Whether mail is configured.</summary>
    public bool CanSendEmail { get; set; }

    /// <summary>Every configured external provider with whether this account has linked a login at it (empty when there is none).</summary>
    public System.Collections.Generic.IReadOnlyList<BaGet.Identity.Application.Models.Account.ExternalLoginStatus> ExternalLogins { get; set; }
        = System.Array.Empty<BaGet.Identity.Application.Models.Account.ExternalLoginStatus>();

    /// <summary>The success message to show.</summary>
    public string? Message { get; set; }

    /// <summary>The refusal to show.</summary>
    public string? Error { get; set; }

    /// <summary>Why the account cannot be deleted yet, when it cannot.</summary>
    public DeletionBlockers? Blockers { get; set; }
}
