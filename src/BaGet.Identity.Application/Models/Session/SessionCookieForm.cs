namespace BaGet.Identity.Application.Models.Session;

/// <summary>
/// The body of <c>POST /api/ui/session/cookie</c> (a same-origin client that signs in with the cookie): the password step (<see cref="UserName"/>,
/// <see cref="Password"/>), or, when the account has two-factor, the second step (<see cref="Ticket"/> with <see cref="Code"/> or <see cref="RecoveryCode"/>).
/// </summary>
public class SessionCookieForm
{
    /// <summary>The username (password step).</summary>
    public string? UserName { get; set; }

    /// <summary>The password (password step).</summary>
    public string? Password { get; set; }

    /// <summary>The <c>twoFactorTicket</c> of the password step (second step).</summary>
    public string? Ticket { get; set; }

    /// <summary>A code of the authenticator app (second step).</summary>
    public string? Code { get; set; }

    /// <summary>A recovery code (second step).</summary>
    public string? RecoveryCode { get; set; }
}
