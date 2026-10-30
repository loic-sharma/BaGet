using System;

namespace BaGet.Identity.Application.Models.Session;

/// <summary>What <c>/api/ui/session</c> answers: a pair of tokens, or the ticket for the second factor of a sign-in.</summary>
public class SessionModel
{
    /// <summary>Authenticates requests to <c>/api/ui</c> (<c>Authorization: Bearer</c>); null while <see cref="TwoFactorRequired"/>.</summary>
    public string? AccessToken { get; set; }

    /// <summary>When the access token stops being accepted (UTC).</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Exchanged for a new pair at <c>/api/ui/session/refresh</c>; each refresh returns a new one and the old one stays valid until it expires.</summary>
    public string? RefreshToken { get; set; }

    /// <summary>When the refresh token stops being accepted (UTC).</summary>
    public DateTime? RefreshExpiresAt { get; set; }

    /// <summary>The signed-in user's name.</summary>
    public string? UserName { get; set; }

    /// <summary>The password was right but the account asks for a second factor: post the <see cref="TwoFactorTicket"/> and a code to <c>/api/ui/session/two-factor</c> or <c>/recovery-code</c>.</summary>
    public bool TwoFactorRequired { get; set; }

    /// <summary>Proves the password step; valid for five minutes, and useless without a code.</summary>
    public string? TwoFactorTicket { get; set; }

    /// <summary>When the ticket stops being accepted (UTC).</summary>
    public DateTime? TwoFactorTicketExpiresAt { get; set; }

    /// <summary>External sign-in only: a single-use-in-practice token (60 seconds) to exchange for the real ones at <c>POST /api/ui/session/exchange</c>.</summary>
    public string? ExchangeToken { get; set; }

    /// <summary>When the exchange token stops being accepted (UTC).</summary>
    public DateTime? ExchangeExpiresAt { get; set; }

    /// <summary>Something to tell the user when there are no tokens yet (external sign-in: the account was created and awaits approval or a confirmed email address).</summary>
    public string? Notice { get; set; }
}
