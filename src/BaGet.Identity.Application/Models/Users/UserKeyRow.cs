using System;
using System.Collections.Generic;

namespace BaGet.Identity.Application.Models.Users;

/// <summary>One of an account's API keys as the administrator sees it; the key itself and its hash are never shown.</summary>
/// <param name="Key">The key's number (what revoking names).</param>
/// <param name="Description">The description the owner gave it.</param>
/// <param name="Scopes">What it may do.</param>
/// <param name="PackageGlobPattern">The package IDs it applies to.</param>
/// <param name="Created">When it was created.</param>
/// <param name="Expires">When it expires, or null.</param>
/// <param name="Repositories">Where it may be used: the repositories it is limited to, or "all repositories".</param>
public sealed record UserKeyRow(int Key, string? Description, IReadOnlyList<string> Scopes, string? PackageGlobPattern, DateTime Created, DateTime? Expires, string Repositories);
