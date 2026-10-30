namespace BaGet.Identity.Application.Services.Sessions;

/// <summary>
/// The response headers that carry a renewed pair of tokens. An action that changes the account's security stamp on purpose (turning two-factor on,
/// resetting the authenticator, changing the password) keeps the <i>current</i> cookie session signed in; for a token session it answers new tokens in these
/// headers instead, and the client replaces the ones it holds. Every other session of the account ends.
/// </summary>
public static class SessionHeaders
{
    /// <summary>The new access token.</summary>
    public const string AccessToken = "X-Session-Access-Token";

    /// <summary>When the new access token expires (UTC, ISO 8601).</summary>
    public const string AccessExpires = "X-Session-Access-Expires";

    /// <summary>The new refresh token.</summary>
    public const string RefreshToken = "X-Session-Refresh-Token";

    /// <summary>When the new refresh token expires (UTC, ISO 8601).</summary>
    public const string RefreshExpires = "X-Session-Refresh-Expires";

    /// <summary>All of them, for <c>Access-Control-Expose-Headers</c>.</summary>
    public static readonly string[] All = { AccessToken, AccessExpires, RefreshToken, RefreshExpires };
}
