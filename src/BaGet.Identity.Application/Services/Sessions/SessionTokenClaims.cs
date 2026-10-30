using System;

namespace BaGet.Identity.Application.Services.Sessions;

/// <summary>What a valid session token says.</summary>
/// <param name="UserId">The account it was issued to.</param>
/// <param name="SecurityStamp">The account's security stamp when it was issued: a different stamp now (password change, sign out everywhere) voids the token.</param>
/// <param name="AuthenticatedAt">When the user proved who they are (the sign-in); a refresh keeps it.</param>
/// <param name="ExpiresAt">When the token stops being accepted.</param>
public sealed record SessionTokenClaims(string UserId, string SecurityStamp, DateTime AuthenticatedAt, DateTime ExpiresAt);
