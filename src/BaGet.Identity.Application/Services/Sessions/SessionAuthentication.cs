using System;
using System.Security.Claims;
using BaGet.Core;

namespace BaGet.Identity.Application.Services.Sessions;

/// <summary>A session token that was accepted: the account it stands for, as the services see a signed-in user.</summary>
/// <param name="User">The claims principal (user id and name), what the cookie session would put in <c>HttpContext.User</c>.</param>
/// <param name="Principal">The same user as the authorization services see them: every scope, no repository limit, <see cref="BaGetPrincipal.IsInteractiveSession"/>.</param>
/// <param name="Account">The account.</param>
/// <param name="AuthenticatedAt">When the user signed in (a refresh keeps it).</param>
public sealed record SessionAuthentication(ClaimsPrincipal User, BaGetPrincipal Principal, BaGetUser Account, DateTime AuthenticatedAt)
{
    /// <summary>When the token stops being accepted.</summary>
    public DateTime ExpiresAt { get; init; }
}
