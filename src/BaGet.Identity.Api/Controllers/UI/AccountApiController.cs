using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Account;
using BaGet.Identity.Application.UseCases.Account;

namespace BaGet.Identity.Controllers.UI;

/// <summary>
/// The JSON twin of the sign-in side of the account screens (<c>/api/ui/account/...</c>): sign in, the two second-factor steps, sign out,
/// registration, email confirmation, the password reset pair and the re-authentication screen. <c>AccountController</c> calls it in process
/// and renders the views. Registration, the confirmation link, forgot and reset password are <see cref="AnonymousSessionEndpointAttribute"/>s: open to an anonymous HTTP caller from this server's origin (or a listed one), who gets no cookie (registration answers the new account's tokens, or nothing). <b>Interactive session only</b> for the rest: signing in sets the same session cookie as the page (same lockout, approval, two-factor and
/// return-URL rules), so over HTTP an API key and an anonymous caller get 403 "use the web screen" (a client without a cookie signs in at <c>POST /api/ui/session</c>); the view controller calls the twin in process.
/// The external provider's challenge and callback are browser-only and have no twin.
/// </summary>
[Route("api/ui/account")]
public sealed class AccountApiController : AccountApiControllerBase
{
    // ----- login

    /// <summary>The sign-in form.</summary>
    [HttpGet("login"), UiApi("Account", "Login")]
    public ActionResult<ScreenResult<LoginViewModel>> Login(string? returnUrl)
        => Respond(UseCase<SignInUseCase>().ShowLogin(returnUrl));

    /// <summary>Signs in. Success sets the session cookie and continues at <c>next.localUrl</c> (the local return URL, else the site root), or at the second-factor screen.</summary>
    [HttpPost("login"), UiApi("Account", "Login")]
    public async Task<ActionResult<ScreenResult<LoginViewModel>>> LoginPost([FromBody] LoginForm? form)
        => Respond(await UseCase<SignInUseCase>().LoginAsync(form));

    // ----- loginwith2fa

    /// <summary>The authenticator code form (back to sign-in without the half-signed-in cookie).</summary>
    [HttpGet("loginwith2fa"), UiApi("Account", "LoginWith2fa")]
    public async Task<ActionResult<ScreenResult<TwoFactorLoginViewModel>>> LoginWith2fa(bool rememberMe, string? returnUrl)
        => Respond(await UseCase<SignInUseCase>().ShowTwoFactorAsync(rememberMe, returnUrl));

    /// <summary>Completes the sign-in with an authenticator code.</summary>
    [HttpPost("loginwith2fa"), UiApi("Account", "LoginWith2fa")]
    public async Task<ActionResult<ScreenResult<TwoFactorLoginViewModel>>> LoginWith2faPost([FromBody] TwoFactorLoginForm? form)
        => Respond(await UseCase<SignInUseCase>().TwoFactorAsync(form));

    // ----- loginwithrecoverycode

    /// <summary>The recovery code form.</summary>
    [HttpGet("loginwithrecoverycode"), UiApi("Account", "LoginWithRecoveryCode")]
    public async Task<ActionResult<ScreenResult<RecoveryCodeLoginViewModel>>> LoginWithRecoveryCode(string? returnUrl)
        => Respond(await UseCase<SignInUseCase>().ShowRecoveryCodeAsync(returnUrl));

    /// <summary>Completes the sign-in with a recovery code (each works once).</summary>
    [HttpPost("loginwithrecoverycode"), UiApi("Account", "LoginWithRecoveryCode")]
    public async Task<ActionResult<ScreenResult<RecoveryCodeLoginViewModel>>> LoginWithRecoveryCodePost([FromBody] RecoveryCodeLoginForm? form)
        => Respond(await UseCase<SignInUseCase>().RecoveryCodeAsync(form));

    // ----- logout

    /// <summary>The sign-out screen.</summary>
    [HttpGet("logout"), UiApi("Account", "Logout")]
    public ActionResult<ScreenResult<EmptyScreen>> Logout()
        => Respond(UseCase<SignInUseCase>().ShowLogout());

    /// <summary>Signs the user out.</summary>
    [HttpPost("logout"), UiApi("Account", "Logout")]
    public async Task<ActionResult<ScreenResult<EmptyScreen>>> LogoutPost()
        => Respond(await UseCase<SignInUseCase>().LogoutAsync());

    // ----- register

    /// <summary>The registration form (404 while <c>Accounts:AllowRegistration</c> is off).</summary>
    [HttpGet("register"), UiApi("Account", "Register"), AnonymousSessionEndpoint]
    public ActionResult<ScreenResult<RegisterViewModel>> Register(string? returnUrl)
        => Respond(UseCase<RegistrationUseCase>().ShowRegister(returnUrl));

    /// <summary>Creates an account (and signs it in when no confirmation or approval is pending).</summary>
    [HttpPost("register"), UiApi("Account", "Register"), AnonymousSessionEndpoint]
    public async Task<ActionResult<ScreenResult<RegisterViewModel>>> RegisterPost([FromBody] RegisterForm? form, CancellationToken cancellationToken)
        => Respond(await UseCase<RegistrationUseCase>().RegisterAsync(form, cancellationToken));

    // ----- confirmemail

    /// <summary>The target of the links in confirmation emails (a GET that confirms the address, like the page).</summary>
    [HttpGet("confirmemail"), UiApi("Account", "ConfirmEmail"), RequiresEmail, AnonymousSessionEndpoint]
    public async Task<ActionResult<ScreenResult<ConfirmEmailViewModel>>> ConfirmEmail(string? userId, string? code, string? email)
        => Respond(await UseCase<RegistrationUseCase>().ConfirmEmailAsync(User, userId, code, email));

    // ----- forgotpassword

    /// <summary>The "forgot password" form.</summary>
    [HttpGet("forgotpassword"), UiApi("Account", "ForgotPassword"), RequiresEmail, AnonymousSessionEndpoint]
    public ActionResult<ScreenResult<ForgotPasswordViewModel>> ForgotPassword()
        => Respond(UseCase<RegistrationUseCase>().ShowForgotPassword());

    /// <summary>Asks for a reset link. The answer is the same whether or not an account matches.</summary>
    [HttpPost("forgotpassword"), UiApi("Account", "ForgotPassword"), RequiresEmail, AnonymousSessionEndpoint]
    public async Task<ActionResult<ScreenResult<ForgotPasswordViewModel>>> ForgotPasswordPost([FromBody] ForgotPasswordForm? form)
        => Respond(await UseCase<RegistrationUseCase>().ForgotPasswordAsync(form, HttpContext.Connection.RemoteIpAddress?.ToString()));

    // ----- resetpassword

    /// <summary>The new-password form behind the link of a reset email.</summary>
    [HttpGet("resetpassword"), UiApi("Account", "ResetPassword"), RequiresEmail, AnonymousSessionEndpoint]
    public ActionResult<ScreenResult<ResetPasswordViewModel>> ResetPassword(string? userId, string? code)
        => Respond(UseCase<RegistrationUseCase>().ShowResetPassword(userId, code));

    /// <summary>Sets the new password from the link; success continues at the sign-in screen.</summary>
    [HttpPost("resetpassword"), UiApi("Account", "ResetPassword"), RequiresEmail, AnonymousSessionEndpoint]
    public async Task<ActionResult<ScreenResult<ResetPasswordViewModel>>> ResetPasswordPost([FromBody] ResetPasswordForm? form)
        => Respond(await UseCase<RegistrationUseCase>().ResetPasswordAsync(form));

    // ----- reauthenticate

    /// <summary>The "confirm it is you" screen (the button that starts the provider's sign-in is browser-only).</summary>
    [HttpGet("reauthenticate"), UiApi("Account", "Reauthenticate"), RequiresSignIn]
    public ActionResult<ScreenResult<ReauthenticateViewModel>> Reauthenticate(string? returnUrl)
        => Respond(ExternalSignInUseCase.ShowReauthenticate(returnUrl));
}
