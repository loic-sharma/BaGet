using System;
using System.Collections.Generic;

namespace BaGet.Identity.Application.Models.Account;

/// <summary>What the two-factor authentication screen shows.</summary>
public sealed class TwoFactorViewModel
{
    /// <summary>Whether two-factor authentication is on.</summary>
    public bool TwoFactorEnabled { get; set; }

    /// <summary>The key to type into an authenticator that cannot scan the QR code.</summary>
    public string? SharedKey { get; set; }

    /// <summary>The QR code of the setup link, an SVG built from the user's own key.</summary>
    public string? QrCodeSvg { get; set; }

    /// <summary>For an account without a password: it signed in again with its provider a moment ago.</summary>
    public bool Reauthenticated { get; set; }

    /// <summary>Whether an external provider is configured to sign in again with.</summary>
    public bool CanReauthenticate { get; set; }

    /// <summary>How many recovery codes are unused.</summary>
    public int RecoveryCodesLeft { get; set; }

    /// <summary>Whether the account has a password (else it signs in through a provider).</summary>
    public bool HasPassword { get; set; } = true;

    /// <summary>The new recovery codes. Shown once, when two-factor authentication is turned on.</summary>
    public IReadOnlyList<string> RecoveryCodes { get; set; } = Array.Empty<string>();

    /// <summary>The refusal to show.</summary>
    public string? Error { get; set; }

    /// <summary>The success message to show.</summary>
    public string? Message { get; set; }
}
