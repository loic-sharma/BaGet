using System;
using System.Collections.Generic;
using BaGet.Core;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace BaGet.Identity.Application.Services.Accounts;

/// <summary>Fills the authentication handler's options for one <see cref="OpenIdConnectProvider"/> (what <c>AddBaGetAccounts</c> registers per provider).</summary>
public static class OpenIdConnectProviderSetup
{
    /// <summary>Request parameters a provider's <c>Parameters</c> can never set or replace: BaGet's own protocol values.</summary>
    internal static readonly HashSet<string> ReservedParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "client_id", "client_secret", "response_type", "response_mode", "scope", "redirect_uri", "state", "nonce",
        "code_challenge", "code_challenge_method", "prompt", "max_age", "id_token_hint",
    };

    /// <summary>Configures <paramref name="options"/> for <paramref name="provider"/>.</summary>
    public static void Configure(OpenIdConnectOptions options, OpenIdConnectProvider provider)
    {
        var source = provider.Options;
        options.SignInScheme = IdentityConstants.ExternalScheme;
        options.Authority = source.Authority;
        options.ClientId = source.ClientId;
        options.ClientSecret = source.ClientSecret;
        options.ResponseType = "code";
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.CallbackPath = provider.CallbackPath;
        options.SignedOutCallbackPath = provider.SignedOutCallbackPath;
        options.RemoteSignOutPath = provider.RemoteSignOutPath;

        foreach (var scope in OpenIdConnectProviders.ExtraScopes(source)) options.Scope.Add(scope);

        // Default claim mapping and ClaimActions keep the id_token's auth_time as it is sent; the re-authentication check relies on it.
        var parameters = source.Parameters;
        if (parameters is { Count: > 0 })
        {
            var previous = options.Events.OnRedirectToIdentityProvider;
            options.Events.OnRedirectToIdentityProvider = async context =>
            {
                if (previous != null) await previous(context);
                ApplyExtraParameters(context.ProtocolMessage, parameters);
            };
        }
    }

    /// <summary>
    /// Adds the extra parameters to an authorization request unless BaGet or the challenge already set that parameter (a re-authentication's
    /// <c>prompt</c> and <c>max_age</c>) or it is one of <see cref="ReservedParameters"/>.
    /// </summary>
    public static void ApplyExtraParameters(OpenIdConnectMessage message, IReadOnlyDictionary<string, string> parameters)
    {
        foreach (var pair in parameters)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || ReservedParameters.Contains(pair.Key)) continue;
            if (message.Parameters.ContainsKey(pair.Key)) continue;

            message.SetParameter(pair.Key, pair.Value);
        }
    }
}
