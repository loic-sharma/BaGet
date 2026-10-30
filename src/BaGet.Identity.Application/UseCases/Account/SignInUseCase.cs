using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using BaGet.Identity.Application.Models.Account;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Identity.Application.UseCases.Account;

/// <summary>
/// The sign-in side of the account screens: the password form, the two second-factor steps and sign-out. The rules are the sign-in pages' always
/// were: lockout, the pending-approval answer (only to the right password), the unconfirmed-email answer and the local-only return URL. Signing in sets
/// the same session cookie whether a view controller or the JSON API calls it (<see cref="SignInManager{TUser}"/> writes it to the request's response).
/// Browser-only steps (the external provider's challenge and callback) are <see cref="ExternalSignInUseCase"/>.
/// </summary>
public sealed class SignInUseCase
{
    internal const string PendingMessage = "Your account is waiting for an administrator to enable it. Try again once it has been approved.";
    internal const string LockedMessage = "The account is locked after too many failed attempts. Try again later.";
    internal const string UnconfirmedMessage = "Confirm your email address before signing in. Use the link we sent you, or reset your password.";

    private readonly SignInManager<BaGetUser> _signIn;
    private readonly UserManager<BaGetUser> _users;
    private readonly IOptions<AccountsOptions> _accounts;
    private readonly IOptions<EmailOptions> _email;
    private readonly ExternalLogins _logins;

    /// <summary>Creates the use case.</summary>
    public SignInUseCase(
        SignInManager<BaGetUser> signIn,
        UserManager<BaGetUser> users,
        IOptions<AccountsOptions> accounts,
        IOptions<EmailOptions> email)
        : this(signIn, users, accounts, email, ExternalLogins.For(users, accounts))
    {
    }

    /// <summary>Creates the use case.</summary>
    public SignInUseCase(
        SignInManager<BaGetUser> signIn,
        UserManager<BaGetUser> users,
        IOptions<AccountsOptions> accounts,
        IOptions<EmailOptions> email,
        ExternalLogins logins)
    {
        _logins = logins ?? throw new ArgumentNullException(nameof(logins));
        _signIn = signIn ?? throw new ArgumentNullException(nameof(signIn));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _email = email ?? throw new ArgumentNullException(nameof(email));
    }

    // ----- the password form

    /// <summary>The sign-in form.</summary>
    public ScreenResult<LoginViewModel> ShowLogin(string? returnUrl)
        => ScreenResult<LoginViewModel>.Ok(Describe(new LoginViewModel { ReturnUrl = returnUrl }));

    /// <summary>The password form was posted. A successful sign-in continues at the (local) return URL; the answer carries no password.</summary>
    public async Task<ScreenResult<LoginViewModel>> LoginAsync(LoginForm? form)
    {
        var model = Describe(new LoginViewModel
        {
            UserName = form?.UserName,
            RememberMe = form?.RememberMe ?? false,
            ReturnUrl = form?.ReturnUrl,
        });

        var errors = ScreenValidator.Validate(form);
        if (errors.Count > 0) return ScreenResult<LoginViewModel>.Invalid(model, errors);

        #pragma warning disable CS8604
        var result = await _signIn.PasswordSignInAsync(form!.UserName, form.Password, form.RememberMe, lockoutOnFailure: true);
        #pragma warning restore CS8604
        if (result.Succeeded)
        {
            return ScreenResult<LoginViewModel>.GoTo(ScreenNext.Local(LocalUrls.Target(form.ReturnUrl)));
        }

        if (result.RequiresTwoFactor)
        {
            return ScreenResult<LoginViewModel>.GoTo(new ScreenNext(
                ScreenCatalog.AccountLoginWith2fa,
                new Dictionary<string, object?> { ["ReturnUrl"] = form.ReturnUrl, ["RememberMe"] = form.RememberMe }));
        }

        if (result.IsNotAllowed && await FindPendingAsync(form) is not null)
        {
            // For an account awaiting approval the sign-in manager answers "not allowed" only to the right password
            // (a wrong one is a counted failure, a locked-out account answers "locked out"), so this message is
            // shown to the right password only and never reads like an unconfirmed email.
            return Refuse(model, PendingMessage);
        }

        return Refuse(
            model,
            result.IsLockedOut
                ? LockedMessage
                : result.IsNotAllowed
                    ? UnconfirmedMessage
                    : "The user name or password is incorrect.");
    }

    private async Task<BaGetUser?> FindPendingAsync(LoginForm form)
    {
        if (string.IsNullOrEmpty(form.UserName) || string.IsNullOrEmpty(form.Password)) return null;

        var user = await _users.FindByNameAsync(form.UserName);

        return user is { PendingApproval: true } ? user : null;
    }

    private static ScreenResult<LoginViewModel> Refuse(LoginViewModel model, string message)
    {
        model.Error = message;

        return ScreenResult<LoginViewModel>.Invalid(model, message);
    }

    /// <summary>Fills what the screen shows from the configuration.</summary>
    internal LoginViewModel Describe(LoginViewModel model)
    {
        var accounts = _accounts.Value;
        model.AllowRegistration = accounts.AllowRegistration;
        var providers = _logins.Configured;
        model.HasExternalProvider = providers.Count > 0;
        model.ExternalProviderName = providers.Count > 0 ? providers[0].DisplayName : null;
        model.ExternalProviders = providers;
        model.EmailIsConfigured = _email.Value.IsConfigured;

        return model;
    }

    // ----- the authenticator code

    /// <summary>Without the half-signed-in cookie there is nothing to complete: back to the first step.</summary>
    public async Task<ScreenResult<TwoFactorLoginViewModel>> ShowTwoFactorAsync(bool rememberMe, string? returnUrl)
    {
        if (await _signIn.GetTwoFactorAuthenticationUserAsync() == null) return ScreenResult<TwoFactorLoginViewModel>.GoTo(new ScreenNext(ScreenCatalog.AccountLogin));

        return ScreenResult<TwoFactorLoginViewModel>.Ok(new TwoFactorLoginViewModel { RememberMe = rememberMe, ReturnUrl = returnUrl });
    }

    /// <summary>The authenticator code was posted.</summary>
    public async Task<ScreenResult<TwoFactorLoginViewModel>> TwoFactorAsync(TwoFactorLoginForm? form)
    {
        if (await _signIn.GetTwoFactorAuthenticationUserAsync() == null) return ScreenResult<TwoFactorLoginViewModel>.GoTo(new ScreenNext(ScreenCatalog.AccountLogin));

        var model = new TwoFactorLoginViewModel { RememberMe = form?.RememberMe ?? false, ReturnUrl = form?.ReturnUrl };
        var errors = ScreenValidator.Validate(form);
        if (errors.Count > 0) return ScreenResult<TwoFactorLoginViewModel>.Invalid(model, errors);

        var code = form!.Code?.Replace(" ", string.Empty).Replace("-", string.Empty);
        #pragma warning disable CS8604
        var result = await _signIn.TwoFactorAuthenticatorSignInAsync(code, form.RememberMe, rememberClient: false);
        #pragma warning restore CS8604

        if (result.Succeeded) return ScreenResult<TwoFactorLoginViewModel>.GoTo(ScreenNext.Local(LocalUrls.Target(form.ReturnUrl)));

        model.Error = result.IsLockedOut ? LockedMessage : "The code is not valid.";

        return ScreenResult<TwoFactorLoginViewModel>.Invalid(model, model.Error);
    }

    // ----- the recovery code

    /// <summary>Without the half-signed-in cookie there is nothing to complete: back to the first step.</summary>
    public async Task<ScreenResult<RecoveryCodeLoginViewModel>> ShowRecoveryCodeAsync(string? returnUrl)
    {
        if (await _signIn.GetTwoFactorAuthenticationUserAsync() == null) return ScreenResult<RecoveryCodeLoginViewModel>.GoTo(new ScreenNext(ScreenCatalog.AccountLogin));

        return ScreenResult<RecoveryCodeLoginViewModel>.Ok(new RecoveryCodeLoginViewModel { ReturnUrl = returnUrl });
    }

    /// <summary>The recovery code was posted. Each code works once.</summary>
    public async Task<ScreenResult<RecoveryCodeLoginViewModel>> RecoveryCodeAsync(RecoveryCodeLoginForm? form)
    {
        if (await _signIn.GetTwoFactorAuthenticationUserAsync() == null) return ScreenResult<RecoveryCodeLoginViewModel>.GoTo(new ScreenNext(ScreenCatalog.AccountLogin));

        var model = new RecoveryCodeLoginViewModel { ReturnUrl = form?.ReturnUrl };
        var errors = ScreenValidator.Validate(form);
        if (errors.Count > 0) return ScreenResult<RecoveryCodeLoginViewModel>.Invalid(model, errors);

        #pragma warning disable CS8604
        var result = await _signIn.TwoFactorRecoveryCodeSignInAsync(form!.RecoveryCode?.Replace(" ", string.Empty));
        #pragma warning restore CS8604

        if (result.Succeeded) return ScreenResult<RecoveryCodeLoginViewModel>.GoTo(ScreenNext.Local(LocalUrls.Target(form.ReturnUrl)));

        model.Error = result.IsLockedOut ? LockedMessage : "The recovery code is not valid, or was already used.";

        return ScreenResult<RecoveryCodeLoginViewModel>.Invalid(model, model.Error);
    }

    // ----- sign out

    /// <summary>The sign-out button (signing out is a POST, so a link elsewhere cannot do it).</summary>
    public ScreenResult<EmptyScreen> ShowLogout() => ScreenResult<EmptyScreen>.Ok(EmptyScreen.Instance);

    /// <summary>Signs the user out and goes to the site root.</summary>
    public async Task<ScreenResult<EmptyScreen>> LogoutAsync()
    {
        await _signIn.SignOutAsync();

        return ScreenResult<EmptyScreen>.GoTo(ScreenNext.Local(LocalUrls.Root));
    }
}
