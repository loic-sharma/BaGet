using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace BaGet.Identity.Application.Services.Accounts;

/// <summary>
/// The global API key, as before, plus users' own keys.
/// </summary>
public class AccountAuthenticationService : Core.IAuthenticationService
{
    private readonly ApiKeyAuthenticationService _globalKey;
    private readonly ApiKeyService _apiKeys;
    private readonly UserManager<BaGetUser> _users;
    private readonly IOptionsSnapshot<BaGetOptions> _options;

    public AccountAuthenticationService(
        ApiKeyAuthenticationService globalKey,
        ApiKeyService apiKeys,
        UserManager<BaGetUser> users,
        IOptionsSnapshot<BaGetOptions> options)
    {
        _globalKey = globalKey ?? throw new ArgumentNullException(nameof(globalKey));
        _apiKeys = apiKeys ?? throw new ArgumentNullException(nameof(apiKeys));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<bool> AuthenticateAsync(string? apiKey, CancellationToken cancellationToken)
        => await AuthenticatePrincipalAsync(apiKey, cancellationToken) != null;

    public async Task<BaGetPrincipal?> AuthenticatePrincipalAsync(string? apiKey, CancellationToken cancellationToken)
    {
        // The global key is the superuser, as it always was - but only when one is
        // configured. An empty ApiKey means "there is no global key", never "every
        // caller is the superuser": enabling accounts is asking for ownership to decide
        // who may push what, and a superuser principal skips ownership entirely, so
        // treating the open-server default as a free pass would make the ownership gate
        // bypassable by simply sending no key at all.
        if (!string.IsNullOrEmpty(_options.Value.ApiKey)
            && await _globalKey.AuthenticateAsync(apiKey, cancellationToken))
        {
            return BaGetPrincipal.Superuser;
        }

        var key = await _apiKeys.FindValidOrNullAsync(apiKey, cancellationToken);
        if (key == null) return null;

        #pragma warning disable CS8604
        var user = await _users.FindByIdAsync(key.UserId);
        #pragma warning restore CS8604
        if (user == null || user.PendingApproval) return null;

        return new BaGetPrincipal(
            user.Id,
            user.UserName,
            isSuperuser: false,
            key.Scopes,
            key.PackageGlobPattern,
            isAdministrator: user.IsAdministrator,
            repositoryKeys: await _apiKeys.RepositoryKeysAsync(key.Key, cancellationToken));
    }
}