using System;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Security;
using Microsoft.AspNetCore.Identity;

namespace BaGet.Identity.Application.Services.Sessions;

/// <summary>
/// Turns a session token into the account it stands for, checking on every use what a stateless token cannot: the account still exists, is not awaiting
/// approval, and its security stamp is the one the token was issued with (so a password change or "sign out everywhere" voids every token).
/// A refusal says nothing about why.
/// </summary>
public sealed class SessionAuthenticator
{
    private readonly SessionTokenService _tokens;
    private readonly UserManager<BaGetUser> _users;

    /// <summary>Creates the authenticator.</summary>
    public SessionAuthenticator(SessionTokenService tokens, UserManager<BaGetUser> users)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _users = users ?? throw new ArgumentNullException(nameof(users));
    }

    /// <summary>The account behind <paramref name="token"/> of <paramref name="kind"/>, or null when the token is not acceptable.</summary>
    public async Task<SessionAuthentication?> AuthenticateAsync(SessionTokenKind kind, string? token)
    {
        var claims = _tokens.Validate(kind, token);
        if (claims == null) return null;

        var account = await _users.FindByIdAsync(claims.UserId);
        
        // Like a cookie session: an account that is locked out (by someone guessing its password) keeps the sessions it has; only a sign-in is refused.
        if (account == null || account.PendingApproval) return null;

        var stamp = _users.SupportsUserSecurityStamp ? await _users.GetSecurityStampAsync(account) : string.Empty;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(stamp ?? string.Empty), Encoding.UTF8.GetBytes(claims.SecurityStamp))) return null;

        var identity = new ClaimsIdentity("BaGetSession", ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, account.Id));
        if (account.UserName != null) identity.AddClaim(new Claim(ClaimTypes.Name, account.UserName));

        return new SessionAuthentication(new ClaimsPrincipal(identity), SessionPrincipalFactory.For(account), account, claims.AuthenticatedAt) { ExpiresAt = claims.ExpiresAt };
    }
}
