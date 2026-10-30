using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Identity.Controllers.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using BaGet.Identity.Application.Models.Account;
using BaGet.Identity.Application.Models.Settings;
using BaGet.Identity.Application.UseCases.Account;

namespace BaGet.Identity.Controllers.FrontEnd;

/// <summary>
/// The signed-in user's own settings: account settings (email, password, delete the account), two-factor authentication and API
/// keys. It replaces the Razor Pages <c>Account/Settings</c>, <c>Account/TwoFactor</c> and <c>Account/ApiKeys</c>: same URLs, same <c>?handler=</c> forms. Every action needs a signed-in
/// user (a visitor is challenged, 404 while accounts are off). The work is done by <see cref="AccountSettingsApiController"/>
/// (the use cases of <c>BaGet.Identity.Application</c>); this controller binds the form, calls it in process and renders the view.
/// </summary>
public sealed class AccountSettingsController : AccountControllerBase
{
    /// <inheritdoc/>
    protected override bool RequiresSignIn => true;

    // ----- /account/settings

    /// <summary>Account settings.</summary>
    [Route("account/settings"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Settings()
        => Render(await Api<AccountSettingsApiController, SettingsViewModel>(api => api.Settings()), "Settings", modelShowsFormErrors: true);

    /// <summary>Change the password.</summary>
    [Route("account/settings"), HttpPost, PageHandler("ChangePassword")]
    public async Task<IActionResult> SettingsChangePassword(string? currentPassword, string newPassword, string confirmPassword)
        => Render(
            await Api<AccountSettingsApiController, SettingsViewModel>(api => api.SettingsChangePassword(
                new ChangePasswordForm { CurrentPassword = currentPassword, NewPassword = newPassword, ConfirmPassword = confirmPassword })),
            "Settings",
            modelShowsFormErrors: true);

    /// <summary>Change the email address (asks the password again).</summary>
    [Route("account/settings"), HttpPost, PageHandler("ChangeEmail")]
    public async Task<IActionResult> SettingsChangeEmail(string? newEmail, string? password)
        => Render(
            await Api<AccountSettingsApiController, SettingsViewModel>(api => api.SettingsChangeEmail(new ChangeEmailForm { NewEmail = newEmail, Password = password })),
            "Settings",
            modelShowsFormErrors: true);

    /// <summary>Delete the account (asks the password again).</summary>
    [Route("account/settings"), HttpPost, PageHandler("Delete")]
    public async Task<IActionResult> SettingsDelete(string? password, CancellationToken cancellationToken)
        => Render(
            await Api<AccountSettingsApiController, SettingsViewModel>(api => api.SettingsDelete(new PasswordConfirmationForm { Password = password }, cancellationToken)),
            "Settings",
            modelShowsFormErrors: true);

    /// <summary>"Link this provider" was posted (browser-only: it sends the user to the provider, after the password check).</summary>
    [Route("account/settings"), HttpPost, PageHandler("LinkExternal")]
    public async Task<IActionResult> SettingsLinkExternal(string? provider, string? password)
    {
        var (challenge, screen) = await HttpContext.RequestServices.GetRequiredService<AccountSettingsUseCase>().StartLinkAsync(
            User,
            provider,
            password,
            Url.Action(nameof(Settings), "AccountSettings", new { handler = "LinkCallback" })!);

        return challenge != null ? Challenge(challenge.Properties, challenge.Scheme) : Render(screen!, "Settings", modelShowsFormErrors: true);
    }

    /// <summary>The provider answered a link (browser-only).</summary>
    [Route("account/settings"), HttpGet, PageHandler("LinkCallback")]
    public async Task<IActionResult> SettingsLinkCallback()
        => Render(await HttpContext.RequestServices.GetRequiredService<AccountSettingsUseCase>().CompleteLinkAsync(User), "Settings", modelShowsFormErrors: true);

    /// <summary>Unlink an external provider (asks the password again).</summary>
    [Route("account/settings"), HttpPost, PageHandler("UnlinkExternal")]
    public async Task<IActionResult> SettingsUnlinkExternal(string? provider, string? password)
        => Render(
            await Api<AccountSettingsApiController, SettingsViewModel>(api => api.SettingsUnlinkExternal(new UnlinkExternalForm { Provider = provider, Password = password })),
            "Settings",
            modelShowsFormErrors: true);

    // ----- /account/twofactor

    /// <summary>Two-factor authentication.</summary>
    [Route("account/twofactor"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> TwoFactor()
        => Render(await Api<AccountSettingsApiController, TwoFactorViewModel>(api => api.TwoFactor()), "TwoFactor", modelShowsFormErrors: true);

    /// <summary>Turn two-factor authentication on with a code from the authenticator.</summary>
    [Route("account/twofactor"), HttpPost, PageHandler("Enable")]
    public async Task<IActionResult> TwoFactorEnable(string? code)
        => Render(
            await Api<AccountSettingsApiController, TwoFactorViewModel>(api => api.TwoFactorEnable(new TwoFactorCodeForm { Code = code })),
            "TwoFactor",
            modelShowsFormErrors: true);

    /// <summary>Turn two-factor authentication off (asks the password again).</summary>
    [Route("account/twofactor"), HttpPost, PageHandler("Disable")]
    public async Task<IActionResult> TwoFactorDisable(string? password)
        => Render(
            await Api<AccountSettingsApiController, TwoFactorViewModel>(api => api.TwoFactorDisable(new PasswordConfirmationForm { Password = password })),
            "TwoFactor",
            modelShowsFormErrors: true);

    /// <summary>Reset the authenticator (asks the password again).</summary>
    [Route("account/twofactor"), HttpPost, PageHandler("Reset")]
    public async Task<IActionResult> TwoFactorReset(string? password)
        => Render(
            await Api<AccountSettingsApiController, TwoFactorViewModel>(api => api.TwoFactorReset(new PasswordConfirmationForm { Password = password })),
            "TwoFactor",
            modelShowsFormErrors: true);

    // ----- /account/apikeys

    /// <summary>The user's API keys and the new-key form.</summary>
    [Route("account/apikeys"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> ApiKeysIndex(CancellationToken cancellationToken)
        => Render(await Api<AccountSettingsApiController, ApiKeysViewModel>(api => api.ApiKeysIndex(cancellationToken)), "ApiKeys");

    /// <summary>Create a key.</summary>
    [Route("account/apikeys"), HttpPost, PageHandler("Create")]
    public async Task<IActionResult> ApiKeysCreate(ApiKeyForm form, CancellationToken cancellationToken)
        => Render(await Api<AccountSettingsApiController, ApiKeysViewModel>(api => api.ApiKeysCreate(form, cancellationToken)), "ApiKeys");

    /// <summary>Revoke a key.</summary>
    [Route("account/apikeys"), HttpPost, PageHandler("Revoke")]
    public async Task<IActionResult> ApiKeysRevoke(int key, CancellationToken cancellationToken)
        => Render(await Api<AccountSettingsApiController, ApiKeysViewModel>(api => api.ApiKeysRevoke(key, cancellationToken)), "ApiKeys");
}
