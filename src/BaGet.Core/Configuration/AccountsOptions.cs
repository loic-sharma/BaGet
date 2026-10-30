using System;
using System.Collections.Generic;

namespace BaGet.Core;

/// <summary>
/// User accounts, package owners and per-user API keys.
/// </summary>
[RestartRequired]
public class AccountsOptions
{
    /// <summary>
    /// Off by default, so that an upgraded server behaves exactly as before: only the
    /// global <see cref="BaGetOptions.ApiKey"/> authenticates.
    /// </summary>
    /// <remarks>
    /// While this is on, a caller must prove who they are: the global
    /// <see cref="BaGetOptions.ApiKey"/> if one is configured, otherwise a user's own key.
    /// An unset <see cref="BaGetOptions.ApiKey"/> means there is no global key, not that
    /// the server is open - otherwise ownership, which is the point of accounts, could be
    /// bypassed by sending no key at all.
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>
    /// Whether anyone may create a local account from the sign-in page.
    /// </summary>
    public bool AllowRegistration { get; set; } = true;

    /// <summary>
    /// Whether an account created by self-service (the registration page, or the first sign-in through the external
    /// provider) stays disabled until an administrator approves it on the Users page. Accounts an administrator
    /// creates are never pending. Off by default: new accounts sign in at once, as before.
    /// </summary>
    public bool RequireApproval { get; set; }

    /// <summary>
    /// User names promoted to administrator at startup, separated by semicolons. Applied
    /// only while the server has no administrator, and only to accounts that already exist,
    /// so it bootstraps the first administrator and nothing more: a listed name registered
    /// later is not promoted, and a demotion is not reversed. See also the "account promote" command.
    /// </summary>
    [RestartRequired]
    public string? InitialAdministrators { get; set; }

    /// <summary>
    /// Whether a server with no users at all creates the administrator "Admin" at startup, with a
    /// well-known default password ("Admin1234!") to change at the first sign-in (every start warns until it is). Turn it off to bootstrap
    /// only through <see cref="InitialAdministrators"/> or the "account promote" command.
    /// </summary>
    [RestartRequired]
    public bool CreateDefaultAdministrator { get; set; } = true;

    /// <summary>
    /// How long an access token of an interactive session (<c>POST /api/ui/session</c>, for a single-page application) is accepted. Short on
    /// purpose: a client renews it with its refresh token. Not shown on the Settings page (set it in <c>appsettings.json</c> or the environment).
    /// </summary>
    [HiddenSetting]
    public TimeSpan SessionTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How long a refresh token of an interactive session is accepted after it was issued; every refresh issues a new one, so a client in regular
    /// use stays signed in. Changing the password or signing out everywhere ends every session at once.
    /// </summary>
    [HiddenSetting]
    public TimeSpan SessionRefreshLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    /// The longest a token session lasts, counted from the sign-in (<c>authenticatedAt</c>), however often it is refreshed: past it the user signs in again.
    /// Not shown on the Settings page.
    /// </summary>
    [HiddenSetting]
    public TimeSpan SessionMaxLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// An external OpenID Connect provider, such as Microsoft Entra ID. Unused unless
    /// <see cref="AccountsOpenIdConnectOptions.Authority"/> is set.
    /// </summary>
    public AccountsOpenIdConnectOptions OpenIdConnect { get; set; } = new();

    /// <summary>
    /// More OpenID Connect providers, by id (<c>Accounts:OpenIdConnectProviders:&lt;id&gt;</c>, see <see cref="OpenIdConnectProviderOptions"/>). The id is the
    /// authentication scheme, the stored login-provider name and part of the callback path <c>/signin-oidc-&lt;id&gt;</c>. Not a row of the Settings page
    /// (its "Sign-in providers" panel edits them); restart-required.
    /// </summary>
    [HiddenSetting]
    public Dictionary<string, OpenIdConnectProviderOptions> OpenIdConnectProviders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class AccountsOpenIdConnectOptions
{
    public string? Authority { get; set; }

    public string? ClientId { get; set; }

    [SensitiveSetting]
    public string? ClientSecret { get; set; }

    /// <summary>
    /// The provider's name on the sign-in button.
    /// </summary>
    public string DisplayName { get; set; } = "OpenID Connect";

    public bool IsConfigured => !string.IsNullOrEmpty(Authority) && !string.IsNullOrEmpty(ClientId);
}