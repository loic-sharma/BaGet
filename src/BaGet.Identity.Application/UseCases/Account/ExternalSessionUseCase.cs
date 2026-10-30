using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Models.Session;
using BaGet.Identity.Application.Screens;
using BaGet.Identity.Application.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace BaGet.Identity.Application.UseCases.Account;

/// <summary>
/// The external provider (OpenID Connect) sign-in for a client that holds tokens: the same rules as the sign-in page (<see cref="ExternalSignInUseCase"/>
/// decides, creating the account the first time, honouring lockout, approval and the confirmed email), but no cookie is set and the answer is an
/// <b>exchange token</b> (60 seconds) instead of the session tokens, because the answer travels in the URL fragment: the client exchanges it at
/// <c>POST /api/ui/session/exchange</c>. Browser-only, like the page: the provider redirects the browser, so the controller sends the challenge and
/// returns to an address <see cref="SessionReturnUrls"/> allowed. Only the external cookie is cleaned up; the browser's own session is not touched.
/// </summary>
public sealed class ExternalSessionUseCase
{
    private readonly ExternalSignInUseCase _external;
    private readonly SessionUseCase _sessions;
    private readonly IHttpContextAccessor _http;
    private readonly SystemTime _time;

    /// <summary>Creates the use case.</summary>
    public ExternalSessionUseCase(ExternalSignInUseCase external, SessionUseCase sessions, IHttpContextAccessor http, SystemTime time)
    {
        _external = external ?? throw new ArgumentNullException(nameof(external));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>The challenge to send to the only configured provider, or null when there is not exactly one.</summary>
    /// <param name="callbackUrl">The address of the callback of this API.</param>
    public ExternalChallenge? Start(string callbackUrl) => _external.StartSignIn(null, callbackUrl);

    /// <summary>The challenge to send to the provider, or null when the provider id is unknown.</summary>
    /// <param name="provider">The provider id.</param>
    /// <param name="callbackUrl">The address of the callback of this API.</param>
    public ExternalChallenge? Start(string provider, string callbackUrl) => string.IsNullOrEmpty(provider) ? null : _external.StartSignIn(provider, callbackUrl);

    /// <summary>The provider answered: an exchange token, a notice (account created, not usable yet), or the reason it was refused.</summary>
    public async Task<ScreenResult<SessionModel>> CompleteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var login = await _external.CompleteForSessionAsync(cancellationToken);

            switch (login.Status)
            {
                case ScreenStatus.Invalid:
                    return ScreenResult<SessionModel>.Invalid(new SessionModel(), login.Errors);

                case ScreenStatus.Ok:
                    // The account was created but cannot sign in yet (approval, confirmation link): there is no session to issue.
                    return ScreenResult<SessionModel>.Ok(new SessionModel { Notice = login.Model?.Notice });
            }

            var user = _external.SignedInUser;
            if (user == null) return ScreenResult<SessionModel>.SignIn("The external sign-in failed.");

            return ScreenResult<SessionModel>.Ok(await _sessions.IssueExchangeAsync(user, _time.UtcNow));
        }
        finally
        {
            // Only the external cookie: the browser may hold an unrelated session of the page.
            var http = _http.HttpContext;
            if (http != null) await http.SignOutAsync(IdentityConstants.ExternalScheme);
        }
    }
}
