using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Models.Account;
using Microsoft.AspNetCore.Authentication;
using System.Linq;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using BaGet.Identity.Application.Links;
using BaGet.Identity.Application.Models.Settings;
using BaGet.Identity.Application.Security;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Identity.Application.UseCases.Account;

/// <summary>
/// The signed-in user's account settings: change the password, change the email address, delete the account. Each form has its own method and
/// takes its fields as parameters, so one form's rules never block another's. A sensitive action asks the password again (an account without one
/// signs in again with its provider: <see cref="AccountConfirmation"/>). Cookie sessions only: an API key never changes credentials.
/// </summary>
public sealed class AccountSettingsUseCase
{
    private readonly UserManager<BaGetUser> _users;
    private readonly SignInManager<BaGetUser> _signIn;
    private readonly AccountDeletionService _deletion;
    private readonly IHttpContextAccessor _http;
    private readonly IOptions<AccountsOptions> _accounts;
    private readonly IOptions<EmailOptions> _email;
    private readonly IOptions<SiteOptions> _site;
    private readonly IEmailSender<BaGetUser> _sender;
    private readonly ExternalLogins _logins;

    /// <summary>Creates the use case.</summary>
    public AccountSettingsUseCase(
        UserManager<BaGetUser> users,
        SignInManager<BaGetUser> signIn,
        AccountDeletionService deletion,
        IHttpContextAccessor http,
        IOptions<AccountsOptions> accounts,
        IOptions<EmailOptions> email,
        IOptions<SiteOptions> site,
        IEmailSender<BaGetUser> sender)
        : this(users, signIn, deletion, http, accounts, email, site, sender, ExternalLogins.For(users, accounts))
    {
    }

    /// <summary>Creates the use case.</summary>
    public AccountSettingsUseCase(
        UserManager<BaGetUser> users,
        SignInManager<BaGetUser> signIn,
        AccountDeletionService deletion,
        IHttpContextAccessor http,
        IOptions<AccountsOptions> accounts,
        IOptions<EmailOptions> email,
        IOptions<SiteOptions> site,
        IEmailSender<BaGetUser> sender,
        ExternalLogins logins)
    {
        _logins = logins ?? throw new ArgumentNullException(nameof(logins));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _signIn = signIn ?? throw new ArgumentNullException(nameof(signIn));
        _deletion = deletion ?? throw new ArgumentNullException(nameof(deletion));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _site = site ?? throw new ArgumentNullException(nameof(site));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
    }

    private bool EmailConfigured => _email.Value.IsConfigured;

    private HttpContext Http => _http.HttpContext ?? throw new InvalidOperationException("The account settings need a request.");

    /// <summary>The settings screen.</summary>
    public async Task<ScreenResult<SettingsViewModel>> ShowAsync(ClaimsPrincipal caller)
    {
        var model = new SettingsViewModel();

        return await LoadAsync(caller, model) ? ScreenResult<SettingsViewModel>.Ok(model) : ScreenResult<SettingsViewModel>.SignIn();
    }

    /// <summary>Change the password.</summary>
    public async Task<ScreenResult<SettingsViewModel>> ChangePasswordAsync(ClaimsPrincipal caller, ChangePasswordForm? form)
    {
        var user = await _users.GetUserAsync(caller);
        if (user == null) return ScreenResult<SettingsViewModel>.SignIn();

        var model = new SettingsViewModel();
        var newPassword = form?.NewPassword;

        if (string.IsNullOrEmpty(newPassword) || newPassword != form?.ConfirmPassword)
        {
            model.Error = "The new passwords do not match.";
        }
        else
        {
            var hasPassword = await _users.HasPasswordAsync(user);

            // Adding the first password to an external-only account would turn a stolen
            // session into a password for it, so it needs the same fresh proof.
            // The current password goes through the same check as every confirmation (lockout first, a wrong one counted), so this
            // form cannot be used to guess it without limit.
            var result = hasPassword
                ? await AccountConfirmation.IsConfirmedAsync(_users, Http, user, form!.CurrentPassword)
                    ? await _users.ChangePasswordAsync(user, form.CurrentPassword ?? string.Empty, newPassword)
                    : IdentityResult.Failed(new IdentityError { Code = "PasswordMismatch", Description = "Incorrect password." })
                : await AccountConfirmation.HasReauthenticatedRecentlyAsync(Http)
                    ? await _users.AddPasswordAsync(user, newPassword)
                    : IdentityResult.Failed(new IdentityError { Description = "Sign in again with your provider first, then set the password." });

            if (result.Succeeded)
            {
                // The security stamp changed; keep this session signed in.
                await _signIn.RefreshSignInAsync(user);
                model.Message = "Your password has been changed.";
            }
            else
            {
                model.Error = AccountConfirmation.Describe(result);
            }
        }

        return await DoneAsync(caller, model);
    }

    /// <summary>
    /// Change the email address. The password is asked again, like for deleting the account: the address is where password resets go, so a
    /// stolen session must not be able to repoint it.
    /// </summary>
    public async Task<ScreenResult<SettingsViewModel>> ChangeEmailAsync(ClaimsPrincipal caller, ChangeEmailForm? form)
    {
        var user = await _users.GetUserAsync(caller);
        if (user == null) return ScreenResult<SettingsViewModel>.SignIn();

        var model = new SettingsViewModel();
        var newEmail = form?.NewEmail?.Trim();

        if (!await AccountConfirmation.IsConfirmedAsync(_users, Http, user, form?.Password))
        {
            model.Error = "The confirmation is incorrect, so the email address was not changed.";
        }
        else if (string.IsNullOrEmpty(newEmail) || !new EmailAddressAttribute().IsValid(newEmail))
        {
            model.Error = "Enter a valid email address.";
        }
        else if (!EmailConfigured)
        {
            var result = await _users.SetEmailAsync(user, newEmail);
            if (result.Succeeded)
            {
                await _signIn.RefreshSignInAsync(user);
                model.Message = "Your email address has been changed.";
            }
            else model.Error = AccountConfirmation.Describe(result);
        }
        else
        {
            var token = await _users.GenerateChangeEmailTokenAsync(user, newEmail);
            var link = AccountLinks.ConfirmEmail(_site, user.Id, token, newEmail);

            try
            {
                await _sender.SendConfirmationLinkAsync(user, newEmail, link);
                model.Message = "We sent a confirmation link to the new address. Your address changes when you follow it.";
            }
            catch (Exception)
            {
                model.Error = "We could not send the confirmation email. Try again later.";
            }
        }

        return await DoneAsync(caller, model);
    }

    /// <summary>
    /// Delete the account. The password is asked again, so a stolen session cannot delete it. An account without a password (external sign-in
    /// only) signs in again with its provider. Success signs the user out and continues at the site root.
    /// </summary>
    public async Task<ScreenResult<SettingsViewModel>> DeleteAsync(ClaimsPrincipal caller, PasswordConfirmationForm? form, CancellationToken cancellationToken)
    {
        var user = await _users.GetUserAsync(caller);
        if (user == null) return ScreenResult<SettingsViewModel>.SignIn();

        var model = new SettingsViewModel();

        if (!await AccountConfirmation.IsConfirmedAsync(_users, Http, user, form?.Password))
        {
            model.Error = "The confirmation is incorrect, so the account was not deleted.";
        }
        else
        {
            var (blockers, result) = await _deletion.DeleteAsync(user, cancellationToken);

            if (blockers.Any)
            {
                model.Blockers = blockers;
                model.Error = "The account cannot be deleted yet.";
            }
            else if (!result.Succeeded)
            {
                model.Error = AccountConfirmation.Describe(result);
            }
            else
            {
                await _signIn.SignOutAsync();

                return ScreenResult<SettingsViewModel>.GoTo(ScreenNext.Local(LocalUrls.Root));
            }
        }

        return await DoneAsync(caller, model);
    }

    // ----- external logins (link and unlink a provider)

    /// <summary>
    /// "Link <paramref name="provider"/>" was posted: after the password check, the challenge that sends the browser to the provider (browser-only; the answer is
    /// <see cref="CompleteLinkAsync"/>). A provider that is not configured is <see cref="ScreenStatus.NotFound"/>.
    /// </summary>
    public async Task<(ExternalChallenge? Challenge, ScreenResult<SettingsViewModel>? Screen)> StartLinkAsync(ClaimsPrincipal caller, string? provider, string? password, string callbackUrl)
    {
        var user = await _users.GetUserAsync(caller);
        if (user == null) return (null, ScreenResult<SettingsViewModel>.SignIn());

        var configured = _logins.Configured.FirstOrDefault(p => string.Equals(p.Id, provider, StringComparison.OrdinalIgnoreCase));
        if (configured == null) return (null, ScreenResult<SettingsViewModel>.NotFound());

        var model = new SettingsViewModel();
        if (!await AccountConfirmation.IsConfirmedAsync(_users, Http, user, password))
        {
            model.Error = "The confirmation is incorrect, so nothing was linked.";

            return (null, await DoneAsync(caller, model));
        }

        if ((await _logins.LinkedAsync(user)).Any(p => p.Id == configured.Id))
        {
            model.Error = $"{configured.DisplayName} is already linked to this account.";

            return (null, await DoneAsync(caller, model));
        }

        // The anti-forgery value of the challenge is the user's id, so an answer started by another session is not accepted.
        var properties = _signIn.ConfigureExternalAuthenticationProperties(configured.Id, callbackUrl, user.Id);

        return (new ExternalChallenge(configured.Id, properties), null);
    }

    /// <summary>The provider answered a link started by <see cref="StartLinkAsync"/>: its account is linked to the signed-in user, unless another account here already has it.</summary>
    public async Task<ScreenResult<SettingsViewModel>> CompleteLinkAsync(ClaimsPrincipal caller)
    {
        var user = await _users.GetUserAsync(caller);
        if (user == null) return ScreenResult<SettingsViewModel>.SignIn();

        var model = new SettingsViewModel();
        var info = await _signIn.GetExternalLoginInfoAsync(user.Id);
        await Http.SignOutAsync(IdentityConstants.ExternalScheme);

        if (info == null || !_logins.Configured.Any(p => p.Id == info.LoginProvider))
        {
            model.Error = "The sign-in with the provider failed, so nothing was linked.";
        }
        else if (await _users.FindByLoginAsync(info.LoginProvider, info.ProviderKey) is { } owner)
        {
            model.Error = owner.Id == user.Id
                ? "That account is already linked to this one."
                : "That account at the provider is already linked to another account here. Unlink it there first.";
        }
        else
        {
            var result = await _users.AddLoginAsync(user, info);
            if (result.Succeeded)
            {
                await _signIn.RefreshSignInAsync(user);
                model.Message = $"{_logins.Configured.First(p => p.Id == info.LoginProvider).DisplayName} is linked to your account.";
            }
            else model.Error = AccountConfirmation.Describe(result);
        }

        return await DoneAsync(caller, model);
    }

    /// <summary>
    /// Unlinks the account login at the form's provider after the password check. Refused when it is the only way to sign in (no password and
    /// no other login): the account would be lost.
    /// </summary>
    public async Task<ScreenResult<SettingsViewModel>> UnlinkAsync(ClaimsPrincipal caller, UnlinkExternalForm? form)
    {
        var user = await _users.GetUserAsync(caller);
        if (user == null) return ScreenResult<SettingsViewModel>.SignIn();

        var configured = _logins.Configured.FirstOrDefault(p => string.Equals(p.Id, form?.Provider, StringComparison.OrdinalIgnoreCase));
        if (configured == null) return ScreenResult<SettingsViewModel>.NotFound();

        var model = new SettingsViewModel();
        var logins = await _users.GetLoginsAsync(user);
        var mine = logins.Where(l => l.LoginProvider == configured.Id).ToList();

        if (!await AccountConfirmation.IsConfirmedAsync(_users, Http, user, form?.Password))
        {
            model.Error = "The confirmation is incorrect, so nothing was unlinked.";
        }
        else if (mine.Count == 0)
        {
            model.Error = $"{configured.DisplayName} is not linked to this account.";
        }
        else if (!await _users.HasPasswordAsync(user)
                 && !logins.Any(l => l.LoginProvider != configured.Id && _logins.Configured.Any(p => p.Id == l.LoginProvider)))
        {
            // Logins at providers that are no longer configured cannot sign in: they do not count as another way in.
            model.Error = "That is the only way to sign in to this account. Set a password first.";
        }
        else
        {
            foreach (var login in mine)
            {
                var result = await _users.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey);
                if (!result.Succeeded)
                {
                    model.Error = AccountConfirmation.Describe(result);
                    break;
                }
            }

            if (model.Error == null)
            {
                // Keep this session signed in whether or not the store touched the security stamp.
                await _signIn.RefreshSignInAsync(user);
                model.Message = $"{configured.DisplayName} was unlinked from your account.";
            }
        }

        return await DoneAsync(caller, model);
    }

    // The screen again with what the action said: a refusal is Invalid (the model carries the message the page prints), anything else Ok.
    private async Task<ScreenResult<SettingsViewModel>> DoneAsync(ClaimsPrincipal caller, SettingsViewModel model)
    {
        await LoadAsync(caller, model);

        return model.Error == null ? ScreenResult<SettingsViewModel>.Ok(model) : ScreenResult<SettingsViewModel>.Invalid(model, model.Error);
    }

    private async Task<bool> LoadAsync(ClaimsPrincipal caller, SettingsViewModel model)
    {
        var user = await _users.GetUserAsync(caller);
        if (user == null) return false;

        model.Email = user.Email;
        model.EmailConfirmed = user.EmailConfirmed;
        model.HasPassword = await _users.HasPasswordAsync(user);
        model.Reauthenticated = !model.HasPassword && await AccountConfirmation.HasReauthenticatedRecentlyAsync(Http);
        model.CanReauthenticate = await _logins.CanReauthenticateAsync(user);
        model.ExternalLogins = await _logins.StatusAsync(user);
        model.CanSendEmail = EmailConfigured;

        return true;
    }
}
