using System;
using System.Security.Claims;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Identity.Application.Security;

/// <summary>
/// Turns a signed-in browser session into a <see cref="BaGetPrincipal"/>, so the browser
/// path is authorized by exactly the rules the API key path is.
/// </summary>
public class SessionPrincipalFactory : ISessionPrincipalFactory
{
    private readonly UserManager<BaGetUser> _users;

    public SessionPrincipalFactory(UserManager<BaGetUser> users)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
    }

    /// <summary>
    /// A session carries every scope - the user could create a key with all of them
    /// anyway - and a glob that matches every ID. Ownership still decides:
    /// <see cref="OwnershipAuthorizationService"/> requires the user to own an existing
    /// ID, so a session may push a new ID or one its user owns, and nothing else. It is
    /// never the superuser; an administrator's session carries <see cref="BaGetPrincipal.IsAdministrator"/>.
    /// </summary>
    /// <returns>Null when nobody is signed in or the account no longer exists.</returns>
    public async Task<BaGetPrincipal?> CreateAsync(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true) return null;

        var userId = _users.GetUserId(user);
        if (string.IsNullOrEmpty(userId)) return null;

        var account = await _users.FindByIdAsync(userId);
        if (account == null || account.PendingApproval) return null;

        return For(account, user.Identity.Name);
    }

    /// <summary>The principal of an interactive session of <paramref name="account"/> (see <see cref="CreateAsync"/>).</summary>
    internal static BaGetPrincipal For(BaGetUser account, string? userName = null)
        => new(
            userId: account.Id,
            userName: userName ?? account.UserName,
            isSuperuser: false,
            scopes: ApiKeyScopes.All,
            packageGlobPattern: "*",
            isAdministrator: account.IsAdministrator,
            isInteractiveSession: true);
}