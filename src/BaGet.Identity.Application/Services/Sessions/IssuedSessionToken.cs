using System;

namespace BaGet.Identity.Application.Services.Sessions;

/// <summary>A token just issued and when it expires.</summary>
/// <param name="Token">The value to send as <c>Authorization: Bearer</c> (never logged or stored).</param>
/// <param name="ExpiresAt">When it stops being accepted (UTC).</param>
public sealed record IssuedSessionToken(string Token, DateTime ExpiresAt);
