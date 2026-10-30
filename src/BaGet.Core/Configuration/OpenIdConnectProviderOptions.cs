using System;
using System.Collections.Generic;

namespace BaGet.Core;

/// <summary>
/// One named OpenID Connect provider (<c>Accounts:OpenIdConnectProviders:&lt;id&gt;</c>). The id is the authentication scheme, the stored
/// login-provider name and part of the callback path (<c>/signin-oidc-&lt;id&gt;</c>). Providers are registered at startup, so a change needs a restart.
/// </summary>
public class OpenIdConnectProviderOptions
{
    /// <summary>The provider's issuer address (its discovery document is read from here).</summary>
    public string? Authority { get; set; }

    /// <summary>The client id registered at the provider.</summary>
    public string? ClientId { get; set; }

    /// <summary>The client secret registered at the provider. Never shown back.</summary>
    [SensitiveSetting]
    public string? ClientSecret { get; set; }

    /// <summary>The provider's name on the sign-in button. Defaults to the id.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Extra scopes to request, separated by spaces or semicolons (<c>openid profile email</c> are always requested).</summary>
    public string? Scopes { get; set; }

    /// <summary>
    /// Extra parameters of the authorization request, such as <c>domain_hint</c>. They never replace a parameter BaGet sets itself (<c>prompt</c> and
    /// <c>max_age</c> of a re-authentication, <c>state</c>, <c>nonce</c>, <c>redirect_uri</c>, <c>client_id</c>, <c>scope</c>, <c>response_type</c> ...).
    /// </summary>
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The claim holding the preferred user name (default <c>preferred_username</c>).</summary>
    public string? UserNameClaim { get; set; }

    /// <summary>The claim holding the email address (default <c>email</c>).</summary>
    public string? EmailClaim { get; set; }

    /// <summary>The claim saying the email address is verified (default <c>email_verified</c>); only <c>true</c> confirms the address.</summary>
    public string? EmailVerifiedClaim { get; set; }

    /// <summary>
    /// The claim type that decides the administrator flag together with <see cref="AdministratorValue"/>. When both are set the provider is
    /// <b>authoritative for the administrator flag</b> of the users who sign in through it: each sign-in promotes when the value is present and demotes
    /// when it is absent (never the last administrator).
    /// </summary>
    public string? AdministratorClaim { get; set; }

    /// <summary>The value of <see cref="AdministratorClaim"/> that makes a user an administrator.</summary>
    public string? AdministratorValue { get; set; }

    /// <summary>The claim holding the user's groups, used with <see cref="GroupMappings"/>.</summary>
    public string? GroupClaim { get; set; }

    /// <summary>
    /// Claim value to BaGet group name. At each sign-in through this provider the user joins the mapped groups whose value is present and leaves the mapped
    /// groups whose value is absent; other groups are never touched and an unknown group is skipped, not created. A value holds no <c>:</c>.
    /// </summary>
    public Dictionary<string, string> GroupMappings { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Whether the provider has what it needs to be offered.</summary>
    public bool IsConfigured => !string.IsNullOrEmpty(Authority) && !string.IsNullOrEmpty(ClientId);
}
