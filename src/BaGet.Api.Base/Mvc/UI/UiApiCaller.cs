using System;
using System.Security.Claims;
using BaGet.Core;
using BaGet.Frontend.Security;
using BaGet.Identity.Application.Security;
using BaGet.Identity.Application.Services.Sessions;
using Microsoft.AspNetCore.Http;

namespace BaGet.Frontend.Mvc.UI;

/// <summary>
/// Who calls <c>/api/ui</c> over HTTP: an API key sent as <c>Authorization: Bearer</c> or <c>X-NuGet-ApiKey</c> (headers a web page cannot make a browser
/// send), a person's interactive session (a session token as <c>Authorization: Bearer bgs.xxx</c>, or the browser cookie accompanied by a valid antiforgery
/// header), or nobody. The cookie alone is never a credential here and neither is HTTP Basic (browsers cache Basic credentials and re-send them on
/// cross-site requests). <see cref="UiApiSecurityFilter"/> decides and records it here; <c>User</c> is then the key's owner (or anonymous), so the gate
/// and the services see one kind of caller. The in-process call of a view controller does not pass through the filter: it has its own cookie session.
/// </summary>
public static class UiApiCaller
{
    private const string KeyItem = "BaGet.UiApi.Key";

    /// <summary>The header that carries the antiforgery request token when the cookie session is used on <c>/api/ui</c> (<c>GET /api/ui/antiforgery</c> hands it out).</summary>
    public const string AntiforgeryHeader = "X-XSRF-TOKEN";

    private const string CookieUserItem = "BaGet.UiApi.CookieUser";

    /// <summary>
    /// The interactive session that authenticated the request (a session token, or the cookie session with a valid antiforgery header), or null.
    /// An API key is never one: it is <see cref="Key"/>.
    /// </summary>
    public static BaGetPrincipal? Interactive(HttpContext context)
        => context.Items.TryGetValue(RequestPrincipalResolver.InteractiveItem, out var session) ? session as BaGetPrincipal : null;

    /// <summary>The cookie session's user the request arrived with, before the filter decided the caller (so the antiforgery token can be issued for it), or null.</summary>
    internal static System.Security.Claims.ClaimsPrincipal? CookieUser(HttpContext context)
        => context.Items.TryGetValue(CookieUserItem, out var user) ? user as ClaimsPrincipal : null;

    internal static void RememberCookieUser(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true) context.Items[CookieUserItem] = context.User;
    }

    /// <summary>
    /// A person's session authenticated the request. A session token (<paramref name="authenticatedAt"/> set) replaces the cookie entirely; the cookie
    /// (already validated with its antiforgery header) keeps <c>User</c> and stays readable for the re-authentication proof.
    /// </summary>
    internal static void SetInteractive(HttpContext context, ClaimsPrincipal user, BaGetPrincipal principal, DateTime? authenticatedAt)
    {
        RequestPrincipalResolver.Forget(context);
        context.Items[RequestPrincipalResolver.InteractiveItem] = principal;
        context.User = user;

        if (authenticatedAt != null)
        {
            context.Items[AccountConfirmation.IgnoreSessionItem] = true;
            context.Items[AccountConfirmation.SessionAuthenticatedItem] = authenticatedAt.Value;
        }
    }

    /// <summary>The API key that authenticated the request, or null for an anonymous caller.</summary>
    public static BaGetPrincipal? Key(HttpContext context)
        => context.Items.TryGetValue(KeyItem, out var key) ? key as BaGetPrincipal : null;

    internal static void SetKey(HttpContext context, BaGetPrincipal key)
    {
        RememberCookieUser(context);
        context.Items[KeyItem] = key;
        MarkSessionIgnored(context);

        // The owner acts: User carries the key owner's id and name, as a session's does. The global key has no owner.
        var identity = new ClaimsIdentity("BaGetApiKey", ClaimTypes.Name, ClaimTypes.Role);
        if (key.UserId != null) identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, key.UserId));
        if (key.UserName != null) identity.AddClaim(new Claim(ClaimTypes.Name, key.UserName));
        context.User = new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// An anonymous caller: whatever the cookie authentication put in <c>User</c> is dropped, the request's resolved principal is forgotten and the
    /// request is marked so that nothing re-reads the session cookie (<see cref="AccountConfirmation.IgnoreSessionItem"/>).
    /// </summary>
    internal static void SetAnonymous(HttpContext context)
    {
        RememberCookieUser(context);
        MarkSessionIgnored(context);
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
    }

    private static void MarkSessionIgnored(HttpContext context)
    {
        context.Items[AccountConfirmation.IgnoreSessionItem] = true;
        RequestPrincipalResolver.Forget(context);
    }

    /// <summary>Whether the request carries an HTTP Basic <c>Authorization</c> header (not accepted on <c>/api/ui</c>).</summary>
    public static bool HasBasicCredentials(HttpRequest request)
        => request.Headers.Authorization.ToString().StartsWith("Basic", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the request carries an API key or a session token in an accepted header (Bearer or <c>X-NuGet-ApiKey</c>).</summary>
    public static bool HasKeyCredentials(HttpRequest request)
        => BasicCredentials.ReadBearer(request) != null
           || !string.IsNullOrEmpty(request.Headers[RequestPrincipalResolver.ApiKeyHeader].ToString());

    /// <summary>The session token of the request's <c>Authorization: Bearer</c> header, or null (an API key, or none).</summary>
    public static string? SessionTokenOf(HttpRequest request)
    {
        var bearer = BasicCredentials.ReadBearer(request);

        return SessionTokenService.IsSessionToken(bearer) ? bearer : null;
    }
}
