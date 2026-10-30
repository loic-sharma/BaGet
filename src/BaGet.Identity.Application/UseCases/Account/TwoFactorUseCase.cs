using System;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using QRCoder;
using BaGet.Identity.Application.Models.Account;
using BaGet.Identity.Application.Security;

using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Identity.Application.UseCases.Account;

/// <summary>
/// Authenticator-app (TOTP) two-factor authentication of the signed-in user: set up with a QR code, recovery codes shown once, turn off, or start
/// over with a new key. The setup key, its QR code and the new recovery codes are in the answer because the screen shows them; the endpoints are
/// cookie sessions only, so an API key can never read them.
/// </summary>
public sealed class TwoFactorUseCase
{
    private const int RecoveryCodeCount = 10;
    private const string Issuer = "BaGet Extension";

    private readonly UserManager<BaGetUser> _users;
    private readonly SignInManager<BaGetUser> _signIn;
    private readonly IHttpContextAccessor _http;
    private readonly ExternalLogins _logins;

    /// <summary>Creates the use case.</summary>
    public TwoFactorUseCase(
        UserManager<BaGetUser> users,
        SignInManager<BaGetUser> signIn,
        IHttpContextAccessor http,
        IOptions<AccountsOptions> accounts)
        : this(users, signIn, http, accounts, ExternalLogins.For(users, accounts))
    {
    }

    /// <summary>Creates the use case.</summary>
    public TwoFactorUseCase(
        UserManager<BaGetUser> users,
        SignInManager<BaGetUser> signIn,
        IHttpContextAccessor http,
        IOptions<AccountsOptions> accounts,
        ExternalLogins logins)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _signIn = signIn ?? throw new ArgumentNullException(nameof(signIn));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _ = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _logins = logins ?? throw new ArgumentNullException(nameof(logins));
    }

    private HttpContext Http => _http.HttpContext ?? throw new InvalidOperationException("Two-factor authentication needs a request.");

    /// <summary>The screen.</summary>
    public async Task<ScreenResult<TwoFactorViewModel>> ShowAsync(ClaimsPrincipal caller)
    {
        var model = new TwoFactorViewModel();

        return await LoadAsync(caller, model) ? ScreenResult<TwoFactorViewModel>.Ok(model) : ScreenResult<TwoFactorViewModel>.SignIn();
    }

    /// <summary>Turn two-factor authentication on with a code from the authenticator.</summary>
    public async Task<ScreenResult<TwoFactorViewModel>> EnableAsync(ClaimsPrincipal caller, TwoFactorCodeForm? form)
    {
        var user = await _users.GetUserAsync(caller);
        if (user == null) return ScreenResult<TwoFactorViewModel>.SignIn();

        var model = new TwoFactorViewModel();

        // Over /api/ui a stolen token must not be enough to take the account over: turning two-factor on asks the password again (the screen's own form is unchanged).
        if (AccountConfirmation.IsHttpSession(Http) && !await AccountConfirmation.IsConfirmedAsync(_users, Http, user, form?.Password))
        {
            model.Error = "Confirm your password to turn two-factor authentication on.";

            return await DoneAsync(caller, model);
        }

        var cleaned = (form?.Code ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);
        var valid = cleaned.Length > 0 && await _users.VerifyTwoFactorTokenAsync(
            user,
            _users.Options.Tokens.AuthenticatorTokenProvider,
            cleaned);

        if (!valid)
        {
            model.Error = "The code is not valid. Check the authenticator app and try again.";
        }
        else
        {
            await _users.SetTwoFactorEnabledAsync(user, true);
            await _signIn.RefreshSignInAsync(user);
            #pragma warning disable CS8604
            model.RecoveryCodes = (await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount)).ToList();
            #pragma warning restore CS8604
            model.Message = "Two-factor authentication is on. Keep these recovery codes somewhere safe: each works once, and they are not shown again.";
        }

        return await DoneAsync(caller, model);
    }

    /// <summary>Turn two-factor authentication off (asks the password again).</summary>
    public async Task<ScreenResult<TwoFactorViewModel>> DisableAsync(ClaimsPrincipal caller, PasswordConfirmationForm? form)
    {
        var user = await _users.GetUserAsync(caller);
        if (user == null) return ScreenResult<TwoFactorViewModel>.SignIn();

        var model = new TwoFactorViewModel();
        if (!await AccountConfirmation.IsConfirmedAsync(_users, Http, user, form?.Password))
        {
            model.Error = "The confirmation is incorrect, so nothing was changed.";
        }
        else
        {
            await _users.SetTwoFactorEnabledAsync(user, false);
            await _signIn.RefreshSignInAsync(user);
            model.Message = "Two-factor authentication is off.";
        }

        return await DoneAsync(caller, model);
    }

    /// <summary>
    /// A new key makes the old authenticator and every recovery code useless; it must be set up and enabled again (asks the password again).
    /// </summary>
    public async Task<ScreenResult<TwoFactorViewModel>> ResetAsync(ClaimsPrincipal caller, PasswordConfirmationForm? form)
    {
        var user = await _users.GetUserAsync(caller);
        if (user == null) return ScreenResult<TwoFactorViewModel>.SignIn();

        var model = new TwoFactorViewModel();
        if (!await AccountConfirmation.IsConfirmedAsync(_users, Http, user, form?.Password))
        {
            model.Error = "The confirmation is incorrect, so nothing was changed.";
        }
        else
        {
            await _users.SetTwoFactorEnabledAsync(user, false);
            await _users.ResetAuthenticatorKeyAsync(user);
            await _signIn.RefreshSignInAsync(user);
            model.Message = "The authenticator was reset. Scan the new code to turn two-factor authentication on again.";
        }

        return await DoneAsync(caller, model);
    }

    private async Task<ScreenResult<TwoFactorViewModel>> DoneAsync(ClaimsPrincipal caller, TwoFactorViewModel model)
    {
        await LoadAsync(caller, model);

        return model.Error == null ? ScreenResult<TwoFactorViewModel>.Ok(model) : ScreenResult<TwoFactorViewModel>.Invalid(model, model.Error);
    }

    private async Task<bool> LoadAsync(ClaimsPrincipal caller, TwoFactorViewModel model)
    {
        var user = await _users.GetUserAsync(caller);
        if (user == null) return false;

        model.TwoFactorEnabled = user.TwoFactorEnabled;
        model.HasPassword = await _users.HasPasswordAsync(user);
        model.Reauthenticated = !model.HasPassword && await AccountConfirmation.HasReauthenticatedRecentlyAsync(Http);
        model.CanReauthenticate = await _logins.CanReauthenticateAsync(user);
        model.RecoveryCodesLeft = await _users.CountRecoveryCodesAsync(user);

        // Over /api/ui the setup key (and the stamp change generating it causes) is only handed to a caller who confirmed their password in X-Confirm-Password.
        if (!model.TwoFactorEnabled && (!AccountConfirmation.IsHttpSession(Http) || await ConfirmedByHeaderAsync(user)))
        {
            var key = await _users.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrEmpty(key))
            {
                await _users.ResetAuthenticatorKeyAsync(user);

                // The security stamp changed with the key; keep this session signed in.
                await _signIn.RefreshSignInAsync(user);
                key = await _users.GetAuthenticatorKeyAsync(user);
            }

            model.SharedKey = Format(key);
            model.QrCodeSvg = RenderQrCode(SetupUri(user.UserName, key));
        }

        return true;
    }

    private async Task<bool> ConfirmedByHeaderAsync(BaGetUser user)
    {
        if (!await _users.HasPasswordAsync(user)) return await AccountConfirmation.HasReauthenticatedRecentlyAsync(Http);

        var password = Http.Request.Headers[AccountConfirmation.ConfirmPasswordHeader].ToString();

        return password.Length > 0 && await AccountConfirmation.IsConfirmedAsync(_users, Http, user, password);
    }

    private static string Format(string? key)
    {
        var result = new StringBuilder();
        #pragma warning disable CS8602
        for (var i = 0; i < key.Length; i += 4)
        #pragma warning restore CS8602
        {
            if (i > 0) result.Append(' ');
            result.Append(key, i, Math.Min(4, key.Length - i));
        }

        return result.ToString();
    }

    #pragma warning disable CS8604
    internal static string SetupUri(string? userName, string? key)
        => $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(userName)}"
           + $"?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6";
    #pragma warning restore CS8604

    internal static string RenderQrCode(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);

        return new SvgQRCode(data).GetGraphic(4);
    }
}
