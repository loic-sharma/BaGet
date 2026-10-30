using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Models.Account;
using Microsoft.AspNetCore.Identity;

namespace BaGet.Identity.Application.Services.Accounts;

/// <summary>What screens need to know about the configured external providers and a user's linked logins.</summary>
public sealed class ExternalLogins
{
    private readonly UserManager<BaGetUser> _users;
    private readonly OpenIdConnectProviderSet _providers;

    /// <summary>Creates the service.</summary>
    public ExternalLogins(UserManager<BaGetUser> users, OpenIdConnectProviderSet providers)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
    }

    /// <summary>The service for the providers <paramref name="accounts"/> configures (what the constructors that predate several providers build their own from).</summary>
    public static ExternalLogins For(UserManager<BaGetUser> users, Microsoft.Extensions.Options.IOptions<AccountsOptions> accounts)
        => new(users, OpenIdConnectProviders.Resolve((accounts ?? throw new ArgumentNullException(nameof(accounts))).Value));

    /// <summary>Every configured provider.</summary>
    public IReadOnlyList<ExternalProviderInfo> Configured
        => _providers.All.Select(p => new ExternalProviderInfo(p.Id, p.DisplayName)).ToList();

    /// <summary>The configured providers <paramref name="user"/> has a linked login at.</summary>
    public async Task<IReadOnlyList<ExternalProviderInfo>> LinkedAsync(BaGetUser? user)
    {
        if (user == null || !_providers.Any) return Array.Empty<ExternalProviderInfo>();

        var logins = await _users.GetLoginsAsync(user);

        return _providers.All
            .Where(p => logins.Any(l => string.Equals(l.LoginProvider, p.Id, StringComparison.Ordinal)))
            .Select(p => new ExternalProviderInfo(p.Id, p.DisplayName))
            .ToList();
    }

    /// <summary>Every configured provider with whether <paramref name="user"/> has linked a login at it.</summary>
    public async Task<IReadOnlyList<ExternalLoginStatus>> StatusAsync(BaGetUser? user)
    {
        var linked = (await LinkedAsync(user)).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);

        return Configured.Select(p => new ExternalLoginStatus(p.Id, p.DisplayName, linked.Contains(p.Id))).ToList();
    }

    /// <summary>Whether <paramref name="user"/> can prove presence again with a provider: one of the configured providers is linked to the account.</summary>
    public async Task<bool> CanReauthenticateAsync(BaGetUser? user) => (await LinkedAsync(user)).Count > 0;
}
