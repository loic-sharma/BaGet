using System;
using BaGet.Identity.Application.Services.Accounts;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Models.Account;
using BaGet.Identity.Application.Models.Session;
using BaGet.Identity.Application.Screens;
using BaGet.Identity.Application.Security;
using BaGet.Identity.Application.UseCases.Account;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace BaGet.Identity.Controllers.UI;

/// <summary>
/// Interactive sessions for a single-page application (<c>/api/ui/session</c>, <c>/api/ui/antiforgery</c>): sign in with the password (and the second
/// factor), refresh, sign out, the external provider, the cookie variant for a same-origin client, and the antiforgery token. These are not the twin of a
/// screen (the sign-in page sets a cookie; here the answer is a pair of tokens), so they carry no <c>[UiApi]</c> and are not part of the parity rules. They
/// answer 404 while accounts are off. Every action is an <see cref="AnonymousSessionEndpointAttribute"/>: an expired session token in the header is no credential
/// on them, and a browser request from another origin is refused unless <c>Cors:AllowedOrigins</c> lists it. Tokens are only ever in a body, a header, or (the external
/// sign-in, as a 60-second exchange token) the URL fragment of the address the application returned to, never in a query string or a log; every answer carrying them is <c>no-store</c>.
/// </summary>
[Route("api/ui")]
public sealed class SessionApiController : ApiControllerBase
{
    private readonly IOptions<CorsSettings> _cors;
    private readonly IOptions<FrontendOptions> _frontend;

    /// <summary>Creates the controller.</summary>
    public SessionApiController(IOptions<CorsSettings> cors, IOptions<FrontendOptions> frontend)
    {
        _cors = cors ?? throw new ArgumentNullException(nameof(cors));
        _frontend = frontend ?? throw new ArgumentNullException(nameof(frontend));
    }

    // ----- password

    /// <summary>
    /// Signs in with the user name and password. Answers the tokens, or <c>twoFactorRequired</c> with a short-lived <c>twoFactorTicket</c> for
    /// <c>/session/two-factor</c> or <c>/session/recovery-code</c>. 401 for wrong credentials, 403 for a locked, unapproved or unconfirmed account.
    /// </summary>
    [HttpPost("session"), AnonymousSessionEndpoint]
    public async Task<ActionResult<ScreenResult<SessionModel>>> SignIn([FromBody] SessionSignInForm? form)
        => Tokens(Disabled() ?? await UseCase<SessionUseCase>().SignInAsync(form));

    /// <summary>Completes a sign-in with a code of the authenticator app.</summary>
    [HttpPost("session/two-factor"), AnonymousSessionEndpoint]
    public async Task<ActionResult<ScreenResult<SessionModel>>> TwoFactor([FromBody] SessionTwoFactorForm? form)
        => Tokens(Disabled() ?? await UseCase<SessionUseCase>().TwoFactorAsync(form));

    /// <summary>Completes a sign-in with a recovery code (each works once).</summary>
    [HttpPost("session/recovery-code"), AnonymousSessionEndpoint]
    public async Task<ActionResult<ScreenResult<SessionModel>>> RecoveryCode([FromBody] SessionRecoveryCodeForm? form)
        => Tokens(Disabled() ?? await UseCase<SessionUseCase>().RecoveryCodeAsync(form));

    /// <summary>Exchanges the refresh token for a new pair of tokens (the authentication time is kept; the session ends <c>Accounts:SessionMaxLifetime</c> after the sign-in).</summary>
    [HttpPost("session/refresh"), AnonymousSessionEndpoint]
    public async Task<ActionResult<ScreenResult<SessionModel>>> Refresh([FromBody] SessionRefreshForm? form)
        => Tokens(Disabled() ?? await UseCase<SessionUseCase>().RefreshAsync(form));

    /// <summary>Exchanges the 60-second <c>exchange_token</c> of an external sign-in's redirect for the tokens (or the two-factor ticket).</summary>
    [HttpPost("session/exchange"), AnonymousSessionEndpoint]
    public async Task<ActionResult<ScreenResult<SessionModel>>> Exchange([FromBody] SessionExchangeForm? form)
        => Tokens(Disabled() ?? await UseCase<SessionUseCase>().ExchangeAsync(form));

    /// <summary>
    /// Ends the session: the client forgets its tokens. With <c>everywhere</c> the account's security stamp changes and every token and cookie
    /// session of the account stops working. The caller is the access token, or the refresh token in the body.
    /// </summary>
    [HttpPost("session/sign-out"), AnonymousSessionEndpoint]
    public async Task<ActionResult<ScreenResult<EmptyScreen>>> SignOutSession([FromBody] SessionSignOutForm? form)
    {
        if (!Gate.AccountsEnabled) return Respond(ScreenResult<EmptyScreen>.NotFound());

        NoStore();

        return Respond(await UseCase<SessionUseCase>().SignOutAsync(InteractiveCaller != null ? User : null, form));
    }

    // ----- the cookie, for a client served from this server's origin

    /// <summary>
    /// Signs in with the <b>cookie</b> (same origin only; needs the antiforgery token of <c>GET /api/ui/antiforgery</c> fetched while signed out, so a cross-site page cannot
    /// sign a visitor in): the rules of <c>POST /session</c>, the second factor in the same endpoint (<c>ticket</c> with <c>code</c> or <c>recoveryCode</c>), but the answer holds no tokens.
    /// Fetch the antiforgery token again afterwards: it is bound to the signed-in user.
    /// </summary>
    [HttpPost("session/cookie"), AnonymousSessionEndpoint(SameOriginOnly = true, RequireAntiforgery = true)]
    public async Task<ActionResult<ScreenResult<SessionModel>>> CookieSignIn([FromBody] SessionCookieForm? form)
        => Tokens(Disabled() ?? await UseCase<SessionUseCase>().CookieSignInAsync(form));

    /// <summary>Signs the cookie session out (same origin; send the cookie with the antiforgery header). 401 for anything but a cookie session.</summary>
    [HttpPost("session/cookie/sign-out"), AnonymousSessionEndpoint(SameOriginOnly = true)]
    public async Task<ActionResult<ScreenResult<EmptyScreen>>> CookieSignOut()
    {
        if (!Gate.AccountsEnabled) return Respond(ScreenResult<EmptyScreen>.NotFound());

        // Only the cookie session: a token session has no cookie, and signing out would end an unrelated one of the browser.
        if (InteractiveCaller == null || HttpContext.Items.ContainsKey(AccountConfirmation.SessionAuthenticatedItem))
        {
            return Respond(ScreenResult<EmptyScreen>.SignIn("Send the session cookie with a valid X-XSRF-TOKEN header."));
        }

        await UseCase<SignInUseCase>().LogoutAsync();
        NoStore();

        return Respond(ScreenResult<EmptyScreen>.Ok(EmptyScreen.Instance));
    }

    // ----- the external providers on offer

    /// <summary>
    /// The external sign-in providers a client can offer before anyone is signed in (the sign-in screen's model is interactive-only): their ids, to put in
    /// <c>/session/external/{id}</c>, and the names for the buttons. An empty list when none is configured.
    /// </summary>
    [HttpGet("session/providers"), AnonymousSessionEndpoint]
    public ActionResult<ScreenResult<SessionProvidersModel>> Providers()
    {
        if (!Gate.AccountsEnabled) return Respond(ScreenResult<SessionProvidersModel>.NotFound());

        return Respond(ScreenResult<SessionProvidersModel>.Ok(new SessionProvidersModel { Providers = UseCase<ExternalLogins>().Configured }));
    }

    // ----- antiforgery for the cookie

    /// <summary>
    /// The antiforgery request token for a same-origin client that uses the cookie session: send it in the <c>X-XSRF-TOKEN</c> header (every method). It
    /// is bound to the signed-in user, so fetch it again after signing in or out. Also sets the antiforgery cookie.
    /// </summary>
    [HttpGet("antiforgery"), AnonymousSessionEndpoint]
    public ActionResult<ScreenResult<AntiforgeryTokenModel>> AntiforgeryToken([FromServices] IAntiforgery? antiforgery)
    {
        if (!Gate.AccountsEnabled || antiforgery == null) return Respond(ScreenResult<AntiforgeryTokenModel>.NotFound());

        // The token is bound to the user the cookie authenticates, which the security filter reset for this (credential-less) request.
        var current = HttpContext.User;
        HttpContext.User = UiApiCaller.CookieUser(HttpContext) ?? current;
        try
        {
            var tokens = antiforgery.GetAndStoreTokens(HttpContext);
            NoStore();

            return Respond(ScreenResult<AntiforgeryTokenModel>.Ok(new AntiforgeryTokenModel { HeaderName = UiApiCaller.AntiforgeryHeader, RequestToken = tokens.RequestToken }));
        }
        finally
        {
            HttpContext.User = current;
        }
    }

    // ----- the confirmation link again

    /// <summary>
    /// Mails a new confirmation link to an account whose address is not confirmed. The answer is the same whether or not an account matches or the mail was allowed
    /// (one per account per ten minutes, and per client); 404 without outgoing mail.
    /// </summary>
    [HttpPost("account/resend-confirmation"), AnonymousSessionEndpoint]
    public async Task<ActionResult<ScreenResult<ForgotPasswordViewModel>>> ResendConfirmation([FromBody] ForgotPasswordForm? form)
    {
        if (!Gate.AccountsEnabled || !Gate.EmailConfigured) return Respond(ScreenResult<ForgotPasswordViewModel>.NotFound());

        return Respond(await UseCase<RegistrationUseCase>().ResendConfirmationAsync(form, HttpContext.Connection.RemoteIpAddress?.ToString()));
    }

    // ----- the external provider

    /// <summary>
    /// Starts the sign-in with the external provider (a browser navigation, not a fetch). The result comes back in the URL fragment of
    /// <paramref name="returnUrl"/>, which must lie under <c>Frontend:SpaBasePath</c> of this server, or be an address of an origin listed in <c>Cors:AllowedOrigins</c>.
    /// </summary>
    [HttpGet("session/external/{provider}"), AnonymousSessionEndpoint]
    public IActionResult External(string provider, string? returnUrl)
    {
        // Not a provider id at all: 404. The return address is judged before the provider is looked up, so a probe learns nothing from the order.
        if (!Gate.AccountsEnabled || OpenIdConnectProviders.ValidateId(provider?.ToLowerInvariant()) != null) return NotFound();
        if (!ReturnUrlAllowed(returnUrl)) return BadRequest(new { error = ReturnUrlMessage });

        var callback = Url.Action(nameof(ExternalCallback), "SessionApi", new { returnUrl }, Request.Scheme);
        var challenge = UseCase<ExternalSessionUseCase>().Start(provider!, callback!);
        if (challenge == null) return NotFound();

        return Challenge(challenge.Properties, challenge.Scheme);
    }

    /// <summary>
    /// The provider's answer: redirects to <paramref name="returnUrl"/> with, in the URL fragment, <c>exchange_token</c> (+ <c>exchange_expires_at</c>, <c>user_name</c>), to exchange within
    /// a minute at <c>POST /api/ui/session/exchange</c>, or <c>notice</c> (account created, not usable yet) or <c>error</c>. No token of the session itself is ever put in a URL.
    /// </summary>
    [HttpGet("session/external-callback"), AnonymousSessionEndpoint]
    public async Task<IActionResult> ExternalCallback(string? returnUrl, CancellationToken cancellationToken)
    {
        if (!Gate.AccountsEnabled) return NotFound();
        if (!ReturnUrlAllowed(returnUrl)) return BadRequest(new { error = ReturnUrlMessage });

        var result = await UseCase<ExternalSessionUseCase>().CompleteAsync(cancellationToken);
        var fragment = new Dictionary<string, string?>();
        var model = result.Model;

        if (result.Status == ScreenStatus.Ok && model?.Notice != null) fragment["notice"] = model.Notice;
        else if (result.Status == ScreenStatus.Ok && model?.ExchangeToken != null)
        {
            fragment["exchange_token"] = model.ExchangeToken;
            fragment["exchange_expires_at"] = Iso(model.ExchangeExpiresAt);
            fragment["user_name"] = model.UserName;
        }
        else
        {
            fragment["error"] = result.Errors.TryGetValue(string.Empty, out var errors) && errors.Length > 0 ? errors[0] : "The external sign-in failed.";
        }

        NoStore();
        Response.Headers["Referrer-Policy"] = "no-referrer";

        return Redirect(returnUrl + "#" + string.Join("&", fragment
            .Where(p => p.Value != null)
            .Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value!))));
    }

    // ----- helpers

    private const string ReturnUrlMessage = "returnUrl must lie under Frontend:SpaBasePath of this server, or be an address of an origin listed in Cors:AllowedOrigins.";

    private bool ReturnUrlAllowed(string? returnUrl)
        => SessionReturnUrls.IsAllowed(
            returnUrl,
            Request.Scheme + "://" + Request.Host,
            SessionReturnUrls.Origins(_cors.Value.AllowedOrigins),
            _frontend.Value.NormalizedBasePath());

    private static string? Iso(DateTime? value) => value?.ToString("O", CultureInfo.InvariantCulture);

    private ScreenResult<SessionModel>? Disabled()
        => Gate.AccountsEnabled ? null : ScreenResult<SessionModel>.NotFound();

    private ActionResult<ScreenResult<SessionModel>> Tokens(ScreenResult<SessionModel> result)
    {
        NoStore();

        return Respond(result);
    }

    private void NoStore()
    {
        Response.Headers[HeaderNames.CacheControl] = "no-store";
        Response.Headers[HeaderNames.Pragma] = "no-cache";
    }
}
