namespace BaGet.Core;

/// <summary>
/// An OpenID Connect provider as registered at startup: the legacy <c>Accounts:OpenIdConnect</c> block (id <c>oidc</c>) or a named one.
/// </summary>
/// <param name="Id">The authentication scheme, the stored login-provider name.</param>
/// <param name="DisplayName">The name on the sign-in button.</param>
/// <param name="Options">The provider's settings.</param>
/// <param name="IsLegacy">True for the single <c>Accounts:OpenIdConnect</c> block.</param>
public sealed record OpenIdConnectProvider(string Id, string DisplayName, OpenIdConnectProviderOptions Options, bool IsLegacy)
{
    /// <summary>The callback path the authentication handler listens on.</summary>
    public string CallbackPath => IsLegacy ? "/signin-oidc" : "/signin-oidc-" + Id;

    /// <summary>The path the provider redirects to after a sign-out.</summary>
    public string SignedOutCallbackPath => IsLegacy ? "/signout-callback-oidc" : "/signout-callback-oidc-" + Id;

    /// <summary>The path a provider's front-channel sign-out calls.</summary>
    public string RemoteSignOutPath => IsLegacy ? "/signout-oidc" : "/signout-oidc-" + Id;
}
