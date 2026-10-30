using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Identity.Application.Screens;
using BaGet.Identity.Controllers.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using BaGet.Identity.Application.Models.Account;
using BaGet.Identity.Application.UseCases.Account;

namespace BaGet.Identity.Controllers.FrontEnd;

/// <summary>
/// The sign-in side of the account screens: sign in (password and external provider), the two second-factor steps, sign out,
/// registration, email confirmation, the password reset pair and re-authentication. It replaces the Razor Pages of
/// <c>Pages/Account</c> of the same names: same URLs, same <c>?handler=</c> forms. The work is done by the JSON twin <see cref="AccountApiController"/> (through the use
/// cases of <c>BaGet.Identity.Application</c>); this controller binds the form, calls the twin in process and renders the view. The external
/// provider's challenge and callback are browser-only steps: they have no twin and call <see cref="ExternalSignInUseCase"/> directly.
/// </summary>
public sealed class AccountController : AccountControllerBase
{
    private ExternalSignInUseCase External => HttpContext.RequestServices.GetRequiredService<ExternalSignInUseCase>();

    // ----- /account/login

    /// <summary>The sign-in form.</summary>
    [Route("account/login"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Login(string? returnUrl)
        => Render(await Api<AccountApiController, LoginViewModel>(api => api.Login(returnUrl)), "Login", modelShowsFormErrors: true);

    /// <summary>The sign-in form was posted.</summary>
    [Route("account/login"), HttpPost, PageHandler]
    public async Task<IActionResult> LoginPost(LoginForm form)
        => Render(await Api<AccountApiController, LoginViewModel>(api => api.LoginPost(form)), "Login", modelShowsFormErrors: true);

    /// <summary>"Sign in with the provider" was posted (browser-only: it sends the visitor to the provider).</summary>
    [Route("account/login"), HttpPost, PageHandler("External")]
    public IActionResult LoginExternal(string? returnUrl)
    {
        // The posted button names the provider (none: the only one configured).
        var challenge = External.StartSignIn(Request.Form["provider"].ToString(), Url.Action(nameof(Login), "Account", new { ReturnUrl = returnUrl, handler = "Callback" })!);

        return challenge == null ? NotFound() : Challenge(challenge.Properties, challenge.Scheme);
    }

    /// <summary>The provider has signed the user in (browser-only).</summary>
    [Route("account/login"), HttpGet, PageHandler("Callback")]
    public async Task<IActionResult> LoginCallback(string? returnUrl, CancellationToken cancellationToken)
        => Render(await External.CompleteSignInAsync(returnUrl, cancellationToken), "Login", modelShowsFormErrors: true);

    // ----- /account/loginwith2fa

    /// <summary>The authenticator code form.</summary>
    [Route("account/loginwith2fa"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> LoginWith2fa(bool rememberMe, string? returnUrl)
        => Render(await Api<AccountApiController, TwoFactorLoginViewModel>(api => api.LoginWith2fa(rememberMe, returnUrl)), "LoginWith2fa", modelShowsFormErrors: true);

    /// <summary>The authenticator code was posted.</summary>
    [Route("account/loginwith2fa"), HttpPost, PageHandler]
    public async Task<IActionResult> LoginWith2faPost(TwoFactorLoginForm form)
        => Render(await Api<AccountApiController, TwoFactorLoginViewModel>(api => api.LoginWith2faPost(form)), "LoginWith2fa", modelShowsFormErrors: true);

    // ----- /account/loginwithrecoverycode

    /// <summary>The recovery code form.</summary>
    [Route("account/loginwithrecoverycode"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> LoginWithRecoveryCode(string? returnUrl)
        => Render(await Api<AccountApiController, RecoveryCodeLoginViewModel>(api => api.LoginWithRecoveryCode(returnUrl)), "LoginWithRecoveryCode", modelShowsFormErrors: true);

    /// <summary>The recovery code was posted.</summary>
    [Route("account/loginwithrecoverycode"), HttpPost, PageHandler]
    public async Task<IActionResult> LoginWithRecoveryCodePost(RecoveryCodeLoginForm form)
        => Render(await Api<AccountApiController, RecoveryCodeLoginViewModel>(api => api.LoginWithRecoveryCodePost(form)), "LoginWithRecoveryCode", modelShowsFormErrors: true);

    // ----- /account/logout

    /// <summary>The sign-out button (signing out is a POST, so a link elsewhere cannot do it).</summary>
    [Route("account/logout"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Logout()
        => Render(await Api<AccountApiController, EmptyScreen>(api => api.Logout()), "Logout", _ => null);

    /// <summary>Signs the user out.</summary>
    [Route("account/logout"), HttpPost, PageHandler]
    public async Task<IActionResult> LogoutPost()
        => Render(await Api<AccountApiController, EmptyScreen>(api => api.LogoutPost()), "Logout", _ => null);

    // ----- /account/register

    /// <summary>The registration form (404 while <c>Accounts:AllowRegistration</c> is off).</summary>
    [Route("account/register"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Register(string? returnUrl)
        => Render(await Api<AccountApiController, RegisterViewModel>(api => api.Register(returnUrl)), "Register");

    /// <summary>The registration form was posted.</summary>
    [Route("account/register"), HttpPost, PageHandler]
    public async Task<IActionResult> RegisterPost(RegisterForm form, CancellationToken cancellationToken)
        => Render(await Api<AccountApiController, RegisterViewModel>(api => api.RegisterPost(form, cancellationToken)), "Register");

    // ----- /account/confirmemail

    /// <summary>The target of the links in confirmation emails.</summary>
    [Route("account/confirmemail"), HttpGet, HttpHead, PageHandler, RequiresEmail]
    public async Task<IActionResult> ConfirmEmail(string? userId, string? code, string? email)
        => Render(await Api<AccountApiController, ConfirmEmailViewModel>(api => api.ConfirmEmail(userId, code, email)), "ConfirmEmail");

    // ----- /account/forgotpassword

    /// <summary>The "forgot password" form.</summary>
    [Route("account/forgotpassword"), HttpGet, HttpHead, PageHandler, RequiresEmail]
    public async Task<IActionResult> ForgotPassword()
        => Render(await Api<AccountApiController, ForgotPasswordViewModel>(api => api.ForgotPassword()), "ForgotPassword");

    /// <summary>The "forgot password" form was posted.</summary>
    [Route("account/forgotpassword"), HttpPost, PageHandler, RequiresEmail]
    public async Task<IActionResult> ForgotPasswordPost(ForgotPasswordForm form)
        => Render(await Api<AccountApiController, ForgotPasswordViewModel>(api => api.ForgotPasswordPost(form)), "ForgotPassword");

    // ----- /account/resetpassword

    /// <summary>The new-password form behind the link of a reset email.</summary>
    [Route("account/resetpassword"), HttpGet, HttpHead, PageHandler, RequiresEmail]
    public async Task<IActionResult> ResetPassword(string? userId, string? code)
        => Render(await Api<AccountApiController, ResetPasswordViewModel>(api => api.ResetPassword(userId, code)), "ResetPassword", modelShowsFormErrors: true);

    /// <summary>The new password was posted.</summary>
    [Route("account/resetpassword"), HttpPost, PageHandler, RequiresEmail]
    public async Task<IActionResult> ResetPasswordPost(ResetPasswordForm form)
        => Render(await Api<AccountApiController, ResetPasswordViewModel>(api => api.ResetPasswordPost(form)), "ResetPassword", modelShowsFormErrors: true);

    // ----- /account/reauthenticate

    /// <summary>The "confirm it is you" screen (only the target of the sign-in-again button; the form is elsewhere).</summary>
    [Route("account/reauthenticate"), HttpGet, HttpHead, PageHandler, RequiresSignIn]
    public async Task<IActionResult> Reauthenticate(string? returnUrl)
        => Render(await Api<AccountApiController, ReauthenticateViewModel>(api => api.Reauthenticate(returnUrl)), "Reauthenticate", modelShowsFormErrors: true);

    /// <summary>"Sign in again" was posted: sends the user to the provider (browser-only).</summary>
    [Route("account/reauthenticate"), HttpPost, PageHandler, RequiresSignIn]
    public async Task<IActionResult> ReauthenticatePost(ReauthenticateForm form)
    {
        var (challenge, screen) = await External.StartReauthenticationAsync(
            User,
            Url.Action(nameof(Reauthenticate), "Account", new { form.ReturnUrl, handler = "Callback" })!,
            form.ReturnUrl,
            form.Provider);

        return challenge != null ? Challenge(challenge.Properties, challenge.Scheme) : Render(screen!, "Reauthenticate", modelShowsFormErrors: true);
    }

    /// <summary>The provider answered (browser-only).</summary>
    [Route("account/reauthenticate"), HttpGet, PageHandler("Callback"), RequiresSignIn]
    public async Task<IActionResult> ReauthenticateCallback(string? returnUrl)
        => Render(await External.CompleteReauthenticationAsync(User, returnUrl), "Reauthenticate", modelShowsFormErrors: true);
}
