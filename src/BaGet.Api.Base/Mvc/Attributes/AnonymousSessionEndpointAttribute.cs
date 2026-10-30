using System;

namespace BaGet.Frontend.Mvc.Attributes;

/// <summary>
/// Marks an action of <c>/api/ui</c> that a client without a session calls to <i>get</i> one or to prepare for it (sign-in, refresh, exchange, antiforgery, registration,
/// forgot / reset password, confirm email): <see cref="BaGet.Frontend.Mvc.UI.UiApiSecurityFilter"/> treats an invalid or expired session token on it as no credential at all (a client that
/// attaches its header to every request can still refresh), refuses a browser request from another origin (the <c>Origin</c> header must be this server's, or, unless
/// <see cref="SameOriginOnly"/>, listed in <c>Cors:AllowedOrigins</c>: with no list a separate-origin page cannot script these endpoints, whatever the CORS policy), and, for an
/// account twin, lets an anonymous HTTP caller through where the twin is otherwise for interactive sessions.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AnonymousSessionEndpointAttribute : Attribute
{
    /// <summary>Only this server's own origin may call it (the cookie endpoints: a cookie is never set for another origin).</summary>
    public bool SameOriginOnly { get; set; }

    /// <summary>A valid antiforgery request token (<c>X-XSRF-TOKEN</c>, from <c>GET /api/ui/antiforgery</c> while signed out) must accompany the request: no login CSRF.</summary>
    public bool RequireAntiforgery { get; set; }
}
