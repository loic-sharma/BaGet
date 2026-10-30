using System;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Email;
using BaGet.Identity.Application.Links;
using BaGet.Identity.Application.Screens;
using BaGet.Identity.Application.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BaGet.Frontend.Mvc.Screens;

/// <summary>
/// The rules of the account screens used by the account controller base (<c>AccountControllerBase</c>), the use cases' link builder and the re-authentication state.
/// </summary>
internal static class AccountSupport
{
    /// <summary>How long a sign-in with the external provider counts as proof of presence for a sensitive action.</summary>
    public static readonly TimeSpan ReauthenticationWindow = AccountConfirmation.ReauthenticationWindow;

    /// <summary>The sign-in cookie property that holds the time (ticks, UTC) of the last re-authentication.</summary>
    public const string ReauthenticatedItem = AccountConfirmation.ReauthenticatedItem;

    /// <summary>
    /// The check behind every sensitive action. An account with a password types it again; one without (external sign-in
    /// only) must have signed in again with its provider a moment ago. The rule lives in <see cref="AccountConfirmation"/>.
    /// </summary>
    public static Task<bool> IsConfirmedAsync(UserManager<BaGetUser> users, HttpContext httpContext, BaGetUser? user, string? password)
        => AccountConfirmation.IsConfirmedAsync(users, httpContext, user, password);

    /// <summary>Whether the session re-authenticated with the external provider within <see cref="ReauthenticationWindow"/>.</summary>
    public static Task<bool> HasReauthenticatedRecentlyAsync(HttpContext httpContext)
        => AccountConfirmation.HasReauthenticatedRecentlyAsync(httpContext);

    /// <summary>The URL to go to after a sign-in: <paramref name="returnUrl"/> when it is local, else the site root.</summary>
    public static string LocalTarget(IUrlHelper url, string? returnUrl)
        => !string.IsNullOrEmpty(returnUrl) && url.IsLocalUrl(returnUrl) ? returnUrl : url.Content("~/") ?? "/";

    /// <summary>
    /// An absolute link to an account screen, for an email. Built only from <c>Site:PublicUrl</c>, never from the request's host
    /// or scheme: see <see cref="EmailLinkPolicy"/>.
    /// </summary>
    public static string AbsoluteLink(HttpContext httpContext, IUrlHelper url, ScreenRef screen, object values)
    {
        var site = httpContext.RequestServices.GetRequiredService<IOptions<SiteOptions>>();
        var publicUrl = site.Value.PublicUrl;

        // Without the server-rendered screens (Frontend:Mvc=false, or Frontend:SpaLinks) the link targets the single-page application's route.
        var spa = AccountLinks.ForScreen(site, screen, values);
        if (spa != null) return spa;

        if (EmailLinkPolicy.TryParsePublicUrl(publicUrl, out var root))
        {
            // The path includes the request's path base, which the public URL replaces.
            var path = url.Action(screen.Action, screen.Controller, values) ?? string.Empty;
            var pathBase = httpContext.Request.PathBase;
            if (pathBase.HasValue && path.StartsWith(pathBase.Value, StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(pathBase.Value.Length);
            }

            return root?.GetLeftPart(UriPartial.Path).TrimEnd('/') + path;
        }

        throw new InvalidOperationException(EmailLinkPolicy.UnavailableWarning);
    }

    /// <summary>Decodes a token written by <c>PasswordResetLinkSender.EncodeToken</c>, or null.</summary>
    public static string? DecodeToken(string? code) => AccountConfirmation.DecodeToken(code);

    /// <summary>The messages of an identity failure as one sentence.</summary>
    public static string Describe(IdentityResult result) => AccountConfirmation.Describe(result);
}
