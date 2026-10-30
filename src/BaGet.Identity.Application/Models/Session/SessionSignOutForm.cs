namespace BaGet.Identity.Application.Models.Session;

/// <summary>The body of <c>POST /api/ui/session/sign-out</c>.</summary>
public class SessionSignOutForm
{
    /// <summary>The refresh token to end the session with when the request carries no access token.</summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// Also ends every other session of the account (cookie sessions and tokens) by changing its security stamp. Without it nothing is
    /// revoked: the tokens are stateless, so the client simply forgets them, and they expire.
    /// </summary>
    public bool Everywhere { get; set; }
}
