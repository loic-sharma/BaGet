using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Security;
using BaGet.Identity.Application.Services.Sessions;
using Microsoft.AspNetCore.Http;
using IAuthenticationService = BaGet.Core.IAuthenticationService;

namespace BaGet.Frontend.Security;

/// <summary>
/// Who is calling: the principal behind the request's credentials, for the read checks. Credentials are, in order, the
/// password of <c>Authorization: Basic</c> (an API key; the user name must be the key owner's when the key belongs to a
/// user), the token of <c>Authorization: Bearer</c> (an API key too: what npm sends), the <c>X-NuGet-ApiKey</c> header,
/// then the browser session. A write (<see cref="ResolveForWriteAsync"/>) never uses the session: a cookie is sent by a
/// browser on its own, so accepting it would let any web page make the user's browser publish or delete. Credentials that do not authenticate are the same
/// as none: a repository that is open to everyone stays readable, one that is not answers 401.
/// </summary>
public class RequestPrincipalResolver
{
    /// <summary>The header NuGet clients send an API key in.</summary>
    internal const string ApiKeyHeader = "X-NuGet-ApiKey";

    /// <summary>
    /// The <see cref="HttpContext.Items"/> key under which <c>/api/ui</c> records the interactive session that authenticated the request (a session token,
    /// or the cookie with a valid antiforgery header). Only the UI API filter sets it, so no other route ever sees a session token as a credential: there
    /// a token (<see cref="BaGet.Identity.Application.Services.Sessions.SessionTokenService.Prefix"/>) is an invalid key.
    /// </summary>
    internal const string InteractiveItem = AccountConfirmation.InteractiveSessionItem;

    private const string ItemKey = "BaGet.RequestPrincipal";
    private const string WriteItemKey = "BaGet.RequestWritePrincipal";

    private readonly IAuthenticationService _authentication;
    private readonly ISessionPrincipalFactory? _sessions;

    public RequestPrincipalResolver(IAuthenticationService authentication, ISessionPrincipalFactory? sessions = null)
    {
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _sessions = sessions;
    }

    /// <summary>Drops what was resolved for the request so far (used when the request's credentials are decided afterwards, as on <c>/api/ui</c>).</summary>
    internal static void Forget(HttpContext context)
    {
        context.Items.Remove(ItemKey);
        context.Items.Remove(WriteItemKey);
    }

    /// <summary>The caller, or null when anonymous. Resolved once per request.</summary>
    public async Task<BaGetPrincipal?> ResolveAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (context.Items.TryGetValue(InteractiveItem, out var interactive) && interactive is BaGetPrincipal session) return session;
        if (context.Items.TryGetValue(ItemKey, out var cached)) return cached as BaGetPrincipal;

        var principal = await ResolveCoreAsync(context, cancellationToken).ConfigureAwait(false);
        context.Items[ItemKey] = principal;
        return principal;
    }

    /// <summary>
    /// The caller of a write (npm publish, tag, deprecate, unpublish), or null when its credentials do not authenticate:
    /// the API key sent as Basic password, Bearer token or <c>X-NuGet-ApiKey</c>, as for NuGet's write routes, and never the
    /// browser session. An open server (no global key, accounts off) authenticates a request with no credentials as the
    /// superuser, as it always did. Resolved once per request.
    /// </summary>
    public async Task<BaGetPrincipal?> ResolveForWriteAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (context.Items.TryGetValue(InteractiveItem, out var interactive) && interactive is BaGetPrincipal session) return session;
        if (context.Items.TryGetValue(WriteItemKey, out var cached)) return cached as BaGetPrincipal;

        var basic = BasicCredentials.Read(context.Request);
        var key = KeyOf(context, basic);

        // A session token is not an API key: outside /api/ui (which resolved it before) it is simply not a credential.
        if (SessionTokenService.IsSessionToken(key))
        {
            context.Items[WriteItemKey] = null;

            return null;
        }

        var principal = await _authentication.AuthenticatePrincipalAsync(key, cancellationToken).ConfigureAwait(false);
        if (principal != null && UserMismatch(principal, basic)) principal = null;

        context.Items[WriteItemKey] = principal;
        return principal;
    }

    private static string? KeyOf(HttpContext context, (string UserName, string Password)? basic)
    {
        var key = basic?.Password;
        if (string.IsNullOrEmpty(key)) key = BasicCredentials.ReadBearer(context.Request);
        if (string.IsNullOrEmpty(key)) key = context.Request.Headers[ApiKeyHeader];

        return key;
    }

    private static bool UserMismatch(BaGetPrincipal principal, (string UserName, string Password)? basic)
    {
        var userName = basic?.UserName;

        return !string.IsNullOrEmpty(userName)
               && principal.UserName != null
               && !string.Equals(userName, principal.UserName, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<BaGetPrincipal?> ResolveCoreAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var basic = BasicCredentials.Read(context.Request);
        var key = KeyOf(context, basic);

        if (!string.IsNullOrEmpty(key) && !SessionTokenService.IsSessionToken(key))
        {
            var principal = await _authentication.AuthenticatePrincipalAsync(key, cancellationToken).ConfigureAwait(false);
            if (principal != null && !UserMismatch(principal, basic)) return principal;
        }

        if (_sessions != null && context.User.Identity?.IsAuthenticated == true)
        {
            return await _sessions.CreateAsync(context.User).ConfigureAwait(false);
        }

        return null;
    }
}
