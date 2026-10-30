using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace BaGet.Identity.Application.Services.Accounts;

/// <summary>
/// <see cref="INpmTokenService"/> over the accounts: <c>npm login</c> trades the user name and password for an API key of
/// that user limited to the repository (and, for a virtual one, its members and deploy repository; <see cref="ApiKeyService.CreateAsync(string?, string?, System.Collections.Generic.IReadOnlyList{string}?, string, DateTime?, System.Collections.Generic.IReadOnlyCollection{int}?, CancellationToken)"/>),
/// which npm then sends as <c>Authorization: Bearer</c>. The password check is the sign-in page's: the same lockout counter, the
/// same confirmed-email rule. An account with two-factor authentication cannot sign in with a password alone, so it is refused
/// here and creates a key on its API keys page. The key has no expiry and is revoked from that page or by <c>npm logout</c>.
/// </summary>
public class NpmTokenService : INpmTokenService
{
    private readonly UserManager<BaGetUser> _users;
    private readonly SignInManager<BaGetUser> _signIn;
    private readonly ApiKeyService _apiKeys;
    private readonly ILogger<NpmTokenService> _logger;
    private readonly IVirtualRepositories? _virtual;

    /// <param name="users">The user store.</param>
    /// <param name="signIn">Checks passwords the way the sign-in page does.</param>
    /// <param name="apiKeys">Creates and revokes keys.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="virtualRepositories">Resolves the members and the deploy repository of a virtual repository.</param>
    public NpmTokenService(
        UserManager<BaGetUser> users,
        SignInManager<BaGetUser> signIn,
        ApiKeyService apiKeys,
        ILogger<NpmTokenService> logger,
        IVirtualRepositories? virtualRepositories = null)
    {
        _virtual = virtualRepositories;
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _signIn = signIn ?? throw new ArgumentNullException(nameof(signIn));
        _apiKeys = apiKeys ?? throw new ArgumentNullException(nameof(apiKeys));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private const string Bad = "Bad username or password.";

    /// <inheritdoc/>
    public async Task<NpmLoginResult> LoginAsync(string? userName, string? password, RepositoryInfo repository, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(password)) return new NpmLoginResult(null, Bad);

        var user = await _users.FindByNameAsync(userName);
        if (user == null) return new NpmLoginResult(null, Bad);

        var result = await _signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (result.IsLockedOut) return new NpmLoginResult(null, "The account is locked out: try again later.", LockedOut: true);
        if (result.IsNotAllowed)
        {
            // For an account awaiting approval the sign-in manager answers "not allowed" to the right password only
            // (a wrong one is a counted failure), so this explanation reveals nothing to a guesser.
            return new NpmLoginResult(null, user.PendingApproval
                ? "The account is waiting for an administrator to enable it."
                : "The account may not sign in yet: confirm its email address first.");
        }
        if (!result.Succeeded) return new NpmLoginResult(null, Bad);

        if (await _users.GetTwoFactorEnabledAsync(user))
        {
            return new NpmLoginResult(null, "This account uses two-factor authentication: create an API key on the account's API keys page and use it as the _authToken.");
        }

        // A virtual repository holds no packages of its own: reading through it asks the caller's rights on each member, and
        // a push goes to its deploy repository, so a token limited to the virtual repository alone could neither read a
        // private member nor push. The token covers the virtual repository and the repositories it answers with.
        var keys = new List<int> { repository.Key };
        if (repository.Kind == RepositoryKind.Virtual && _virtual != null)
        {
            keys.AddRange((await _virtual.EffectiveMembersAsync(repository, cancellationToken)).Select(m => m.Key));
            var deploy = await _virtual.DeployTargetAsync(repository, cancellationToken);
            if (deploy != null) keys.Add(deploy.Key);
        }

        var token = await _apiKeys.CreateAsync(
            user.Id,
            $"npm login ({repository.Name})",
            ApiKeyScopes.All,
            "*",
            expires: null,
            repositoryKeys: keys.Distinct().ToList(),
            cancellationToken);

        _logger.LogInformation("{UserName} logged in to the npm registry of {Repository}.", user.UserName, repository.Name);

        return new NpmLoginResult(token);
    }

    /// <inheritdoc/>
    public async Task<bool> RevokeAsync(BaGetPrincipal caller, string token, CancellationToken cancellationToken)
    {
        if (caller == null) throw new ArgumentNullException(nameof(caller));

        var key = await _apiKeys.FindValidOrNullAsync(token, cancellationToken);
        if (key == null || caller.UserId == null || key.UserId != caller.UserId) return false;

        return await _apiKeys.RevokeAsync(caller.UserId, key.Key, cancellationToken);
    }
}
