using System;
using System.Text;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using BaGet.Identity.Application.Email;

namespace BaGet.Identity.Application.Security;

/// <summary>
/// The rules behind every sensitive account action (change email, set a password, delete the account, turn two-factor off or reset it), shared
/// by the account use cases and by the screens that have not moved yet (<c>AccountSupport</c> forwards here).
/// </summary>
public static class AccountConfirmation
{
    /// <summary>How long a sign-in with the external provider counts as proof of presence for a sensitive action.</summary>
    public static readonly TimeSpan ReauthenticationWindow = TimeSpan.FromMinutes(5);

    /// <summary>The sign-in cookie property that holds the time (ticks, UTC) of the last re-authentication.</summary>
    public const string ReauthenticatedItem = "baget.reauthenticated";

    /// <summary>
    /// The check behind every sensitive action. An account with a password types it again; one without (external sign-in
    /// only) must have signed in again with its provider a moment ago. A locked-out account never confirms; a wrong password is counted.
    /// </summary>
    public static async Task<bool> IsConfirmedAsync(UserManager<BaGetUser> users, HttpContext httpContext, BaGetUser? user, string? password)
    {
        #pragma warning disable CS8604
        if (!await users.HasPasswordAsync(user)) return await HasReauthenticatedRecentlyAsync(httpContext);

        // Like a sign-in: a locked-out account is refused without looking at the password, and a wrong one counts as a failed attempt
        // (a refused write while the read-only gate is closed), so confirmations cannot be used to guess a password without limit.
        if (await users.IsLockedOutAsync(user)) return false;
        if (await users.CheckPasswordAsync(user, password ?? string.Empty)) return true;
        await users.AccessFailedAsync(user);

        return false;
        #pragma warning restore CS8604
    }

    /// <summary>
    /// The <see cref="HttpContext.Items"/> key that marks a request whose browser session cookie is not a credential (a JSON twin called over HTTP, which
    /// accepts API keys and session tokens, or the cookie only with an antiforgery header): nothing may read the cookie for it.
    /// </summary>
    public const string IgnoreSessionItem = "BaGet.IgnoreSessionCookie";

    /// <summary>
    /// The <see cref="HttpContext.Items"/> key (a <see cref="DateTime"/>, UTC) that holds when the user of a session token (<c>/api/ui/session</c>) signed in:
    /// that time stands for the cookie's re-authentication property, so an account without a password confirms a sensitive action within
    /// <see cref="ReauthenticationWindow"/> of its (external provider) sign-in.
    /// </summary>
    public const string SessionAuthenticatedItem = "BaGet.SessionAuthenticatedAt";

    /// <summary>The <see cref="HttpContext.Items"/> key set while a request is served by a JSON twin over HTTP (the in-process call of a view controller never has it).</summary>
    public const string OverHttpItem = "BaGet.UiApi.OverHttp";

    /// <summary>The <see cref="HttpContext.Items"/> key under which a JSON twin over HTTP records the interactive session that authenticated the request.</summary>
    public const string InteractiveSessionItem = "BaGet.UiApi.Interactive";

    /// <summary>Whether the request is an interactive session (token, or cookie with the antiforgery header) calling a JSON twin over HTTP.</summary>
    public static bool IsHttpSession(HttpContext httpContext) => httpContext.Items.ContainsKey(InteractiveSessionItem);

    /// <summary>Whether the request is served by a JSON twin over HTTP.</summary>
    public static bool IsOverHttp(HttpContext httpContext) => httpContext.Items.ContainsKey(OverHttpItem);

    /// <summary>The header that carries the password to confirm a GET over HTTP (as on the administration API).</summary>
    public const string ConfirmPasswordHeader = "X-Confirm-Password";

    /// <summary>Whether the session re-authenticated with the external provider within <see cref="ReauthenticationWindow"/>.</summary>
    public static async Task<bool> HasReauthenticatedRecentlyAsync(HttpContext httpContext)
    {
        // A session token carries the time of its sign-in; the cookie is not read for it.
        if (httpContext.Items.TryGetValue(SessionAuthenticatedItem, out var signedInAt) && signedInAt is DateTime signedIn)
        {
            var sinceSignIn = httpContext.RequestServices.GetRequiredService<SystemTime>().UtcNow - signedIn;

            return sinceSignIn >= TimeSpan.Zero && sinceSignIn <= ReauthenticationWindow;
        }

        // A request that does not use the session cookie (/api/ui over HTTP with a key) must not read it here either.
        if (httpContext.Items.ContainsKey(IgnoreSessionItem)) return false;

        var authentication = await httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (authentication?.Properties == null
            || !authentication.Properties.Items.TryGetValue(ReauthenticatedItem, out var value)
            || !long.TryParse(value, out var ticks)
            || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
        {
            return false;
        }

        var age = httpContext.RequestServices.GetRequiredService<SystemTime>().UtcNow - new DateTime(ticks, DateTimeKind.Utc);

        return age >= TimeSpan.Zero && age <= ReauthenticationWindow;
    }

    /// <summary>Identity's tokens carry characters that do not survive a URL, so they travel encoded.</summary>
    public static string EncodeToken(string token) => PasswordResetLinkSender.EncodeToken(token);

    /// <summary>Decodes a token written by <see cref="EncodeToken"/>, or null.</summary>
    public static string? DecodeToken(string? code)
    {
        try
        {
            #pragma warning disable CS8604
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            #pragma warning restore CS8604
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>The messages of an identity failure as one sentence.</summary>
    public static string Describe(IdentityResult result)
        => string.Join(" ", System.Linq.Enumerable.Select(result.Errors, e => e.Description));
}
