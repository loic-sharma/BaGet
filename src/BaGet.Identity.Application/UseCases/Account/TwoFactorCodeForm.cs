namespace BaGet.Identity.Application.UseCases.Account;

/// <summary>What the "turn two-factor authentication on" form posts.</summary>
public sealed class TwoFactorCodeForm
{
    /// <summary>The password, asked again. Needed over <c>/api/ui</c> (a session token or the cookie with an antiforgery header); the screen's form does not post it.</summary>
    public string? Password { get; set; }

    /// <summary>The code from the authenticator app.</summary>
    public string? Code { get; set; }
}
