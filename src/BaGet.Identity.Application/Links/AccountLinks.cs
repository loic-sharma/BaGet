using System;
using System.Linq;
using BaGet.Core;
using Microsoft.Extensions.Options;
using BaGet.Identity.Application.Email;
using BaGet.Identity.Application.Security;

namespace BaGet.Identity.Application.Links;

/// <summary>
/// The absolute links the account emails carry. Built only from <c>Site:PublicUrl</c>, never from the request's host or scheme (see
/// <see cref="EmailLinkPolicy"/>), so a use case that has no request URL helper still writes the same links the screens always wrote.
/// </summary>
public static class AccountLinks
{
    /// <summary>The address of the confirmation page (<c>/account/confirmemail</c>).</summary>
    public const string ConfirmEmailPath = "/account/confirmemail";

    /// <summary>The address of the new-password page (<c>/account/resetpassword</c>).</summary>
    public const string ResetPasswordPath = "/account/resetpassword";

    /// <summary>
    /// The link to <paramref name="path"/> (an application-relative path starting with <c>/</c>) with the query values in order. Throws
    /// when the server has no valid public URL: nothing is mailed then.
    /// </summary>
    public static string Build(IOptions<SiteOptions> site, string path, params (string Name, string? Value)[] query)
    {
        if (site == null) throw new ArgumentNullException(nameof(site));

        path = SpaPath(site.Value.SpaLinkBase, path);

        // An application hosted elsewhere is linked by its own absolute address; everything else lives under the site's public URL.
        var absolute = path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        Uri? root = null;
        if (!absolute && !EmailLinkPolicy.TryParsePublicUrl(site.Value.PublicUrl, out root))
        {
            throw new InvalidOperationException(EmailLinkPolicy.UnavailableWarning);
        }

        var queryString = string.Join(
            "&",
            query.Where(q => q.Value != null).Select(q => Uri.EscapeDataString(q.Name) + "=" + Uri.EscapeDataString(q.Value!)));

        return (absolute ? string.Empty : root?.GetLeftPart(UriPartial.Path).TrimEnd('/')) + path + (queryString.Length == 0 ? string.Empty : "?" + queryString);
    }

    /// <summary>The route of the single-page application that stands for <paramref name="path"/> (<c>Frontend:SpaLinks</c>); the screen's path when it serves emails.</summary>
    public static string SpaPath(string? spaLinkBase, string path)
    {
        if (spaLinkBase == null) return path;

        var route = path switch
        {
            ConfirmEmailPath => "confirm-email",
            ResetPasswordPath => "reset-password",
            "/account/login" => "sign-in",
            _ => null,
        };

        return route == null ? path : spaLinkBase.TrimEnd('/') + "/" + route;
    }

    /// <summary>
    /// The absolute link to a screen for an email when emails link to the single-page application: its route under the base path with the screen's route values
    /// as the query. Null when the screen has no route there (the caller builds the screen's own link).
    /// </summary>
    public static string? ForScreen(IOptions<SiteOptions> site, Screens.ScreenRef screen, object values)
    {
        if (site.Value.SpaLinkBase == null) return null;

        var path = screen.Equals(Screens.ScreenCatalog.AccountResetPassword) ? ResetPasswordPath
            : screen.Equals(Screens.ScreenCatalog.AccountConfirmEmail) ? ConfirmEmailPath
            : screen.Equals(Screens.ScreenCatalog.AccountLogin) ? "/account/login"
            : null;
        if (path == null) return null;

        var query = values.GetType().GetProperties()
            .Select(p => (Name: p.Name, Value: p.GetValue(values)?.ToString()))
            .ToArray();

        return Build(site, path, query);
    }

    /// <summary>The confirmation link of a new account's address, or (with <paramref name="email"/>) of a change of address.</summary>
    public static string ConfirmEmail(IOptions<SiteOptions> site, string userId, string token, string? email = null)
        => Build(site, ConfirmEmailPath, ("userId", userId), ("email", email), ("code", AccountConfirmation.EncodeToken(token)));

    /// <summary>The template of the password-reset link: the user id and the code are filled in later (<see cref="PasswordResetLinkSender"/>).</summary>
    public static string ResetPasswordTemplate(IOptions<SiteOptions> site)
        => Build(site, ResetPasswordPath, ("userId", PasswordResetLinkSender.UserIdPlaceholder), ("code", PasswordResetLinkSender.CodePlaceholder));
}
