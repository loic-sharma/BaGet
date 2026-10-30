using System;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.Screens;
using BaGet.Identity.Application.Email;
using BaGet.Identity.Application.Screens;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Identity.Application.Services.Ownership;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BaGet.Frontend.Controllers.Base;

/// <summary>
/// Shared by the account screens. The screens are always compiled in, but
/// answer 404 while accounts are disabled, so their services are resolved per request rather than injected: they are only
/// registered when accounts are enabled.
/// <para>
/// <see cref="OnActionExecuting"/> checks, in this order: accounts enabled (else 404), mail
/// configured when <see cref="RequiresEmail"/> (else 404), signed in when <see cref="RequiresSignIn"/> (else a challenge, which
/// sends the visitor to sign in). It is checked here rather than with <c>[Authorize]</c> because while accounts are disabled
/// there is no scheme to challenge with and the screen must answer 404 instead.
/// </para>
/// </summary>
public abstract class AccountControllerBase : UiControllerBase
{
    /// <summary>The accounts options.</summary>
    protected AccountsOptions Accounts
        => HttpContext.RequestServices.GetRequiredService<IOptions<AccountsOptions>>().Value;

    /// <summary>Signs users in and out.</summary>
    protected SignInManager<BaGetUser> SignInManager
        => HttpContext.RequestServices.GetRequiredService<SignInManager<BaGetUser>>();

    /// <summary>Users, passwords, tokens.</summary>
    protected UserManager<BaGetUser> UserManager
        => HttpContext.RequestServices.GetRequiredService<UserManager<BaGetUser>>();

    /// <summary>Package and organization ownership.</summary>
    protected PackageOwnershipService Ownership
        => HttpContext.RequestServices.GetRequiredService<PackageOwnershipService>();

    /// <summary>API keys.</summary>
    protected ApiKeyService ApiKeys
        => HttpContext.RequestServices.GetRequiredService<ApiKeyService>();

    /// <summary>Whether outgoing mail is configured.</summary>
    protected bool EmailConfigured
        => HttpContext.RequestServices.GetRequiredService<IOptions<EmailOptions>>().Value.IsConfigured;

    /// <summary>Sends the account emails.</summary>
    protected IEmailSender<BaGetUser> EmailSender
        => HttpContext.RequestServices.GetRequiredService<IEmailSender<BaGetUser>>();

    /// <summary>Screens that only make sense when there is a way to send mail answer 404 otherwise.</summary>
    protected virtual bool RequiresEmail => false;

    /// <summary>Whether the screen needs a signed-in user (checked here, not with <c>[Authorize]</c>, see the class remarks).</summary>
    protected virtual bool RequiresSignIn => false;

    /// <summary>The gate every account and administration screen passes (the JSON API bases ask the same one).</summary>
    protected ScreenGate Gate
        => HttpContext.RequestServices.GetService<ScreenGate>()
           ?? ActivatorUtilities.CreateInstance<ScreenGate>(HttpContext.RequestServices);

    /// <inheritdoc/>
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var metadata = context.ActionDescriptor.EndpointMetadata ?? (System.Collections.Generic.IList<object>)Array.Empty<object>();
        var request = new GateRequest(
            RequiresSignIn: RequiresSignIn || metadata.OfType<RequiresSignInAttribute>().Any(),
            RequiresEmail: RequiresEmail || metadata.OfType<RequiresEmailAttribute>().Any());

        switch (Gate.CheckAccount(User, request))
        {
            case GateOutcome.Allowed:
                base.OnActionExecuting(context);
                break;
            case GateOutcome.SignIn:
                context.Result = Challenge();
                break;
            default:
                context.Result = NotFound();
                break;
        }
    }

    private AccountScreen? _screen;

    /// <summary>The request as the account workflows see it.</summary>
    internal AccountScreen Screen => _screen ??= new AccountScreen(HttpContext, Url, ModelState);

    /// <summary>Turns a workflow's decision into this controller's action result; <paramref name="view"/> renders the screen.</summary>
    internal IActionResult Respond(ScreenOutcome outcome, string view, object? model = null)
        => outcome.ToResult(Url, () => View(view, model));

    /// <summary>Only follow a local return URL, so a sign-in link cannot redirect elsewhere.</summary>
    protected IActionResult RedirectToLocal(string? returnUrl)
        => LocalRedirect(AccountSupport.LocalTarget(Url, returnUrl));

    /// <summary>Identity's tokens carry characters that do not survive a URL, so they travel encoded.</summary>
    protected static string EncodeToken(string token)
        => PasswordResetLinkSender.EncodeToken(token);

    /// <summary>Decodes a token written by <see cref="EncodeToken"/>, or null.</summary>
    protected static string? DecodeToken(string? code)
        => AccountSupport.DecodeToken(code);

    /// <summary>
    /// An absolute link to an account screen, for an email. Built only from <c>Site:PublicUrl</c>, never from the request's host or
    /// scheme: see <see cref="EmailLinkPolicy"/>.
    /// </summary>
    protected string AbsoluteLink(ScreenRef screen, object values)
        => AccountSupport.AbsoluteLink(HttpContext, Url, screen, values);

    /// <summary>How long a sign-in with the external provider counts as proof of presence for a sensitive action.</summary>
    public static TimeSpan ReauthenticationWindow => AccountSupport.ReauthenticationWindow;

    /// <summary>
    /// The check behind every sensitive action (change email, set a password, delete the account, turn two-factor off or reset
    /// it). An account with a password types it again; one without (external sign-in only) must have signed in again with its
    /// provider a moment ago.
    /// </summary>
    protected Task<bool> IsConfirmedAsync(BaGetUser? user, string? password)
        => AccountSupport.IsConfirmedAsync(UserManager, HttpContext, user, password);

    /// <summary>Whether the session re-authenticated with the external provider within <see cref="ReauthenticationWindow"/>.</summary>
    protected Task<bool> HasReauthenticatedRecentlyAsync()
        => AccountSupport.HasReauthenticatedRecentlyAsync(HttpContext);

    /// <summary>The messages of an identity failure as one sentence.</summary>
    protected static string Describe(IdentityResult result)
        => AccountSupport.Describe(result);
}
