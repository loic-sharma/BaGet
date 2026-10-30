namespace BaGet.Identity.Application.Models.Session;

/// <summary>What <c>GET /api/ui/antiforgery</c> answers: the request token to send with a cookie session.</summary>
public class AntiforgeryTokenModel
{
    /// <summary>The header to send the token in (<c>X-XSRF-TOKEN</c>).</summary>
    public string? HeaderName { get; set; }

    /// <summary>The request token. It is bound to the signed-in user: ask again after signing in or out.</summary>
    public string? RequestToken { get; set; }
}
