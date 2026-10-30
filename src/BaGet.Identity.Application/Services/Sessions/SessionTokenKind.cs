namespace BaGet.Identity.Application.Services.Sessions;

/// <summary>What a session token may be used for. Each kind is protected under its own purpose, so one can never be presented as another.</summary>
public enum SessionTokenKind
{
    /// <summary>Authenticates a request to <c>/api/ui</c> as the signed-in user.</summary>
    Access,

    /// <summary>Only exchanged for a new pair of tokens (<c>POST /api/ui/session/refresh</c>).</summary>
    Refresh,

    /// <summary>Proves the password step of a sign-in with two-factor authentication; only completes it with a code.</summary>
    TwoFactor,

    /// <summary>Handed to the browser in the URL fragment after the external provider's sign-in and exchanged at <c>POST /api/ui/session/exchange</c> within a minute: keeps the real tokens out of URLs.</summary>
    Exchange,
}
