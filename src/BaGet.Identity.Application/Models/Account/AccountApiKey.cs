using System;
using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Account;

/// <summary>One API key as the screen lists it: never the key itself or its hash (only the creation response shows a new key, once).</summary>
public sealed class AccountApiKey
{
    /// <summary>The key's id (the number the revoke button posts).</summary>
    public int Key { get; set; }

    /// <summary>What the key is for.</summary>
    public string? Description { get; set; }

    /// <summary>What the key may do.</summary>
    public IReadOnlyList<string> Scopes { get; set; } = Array.Empty<string>();

    /// <summary>The package IDs the key applies to.</summary>
    public string? PackageGlobPattern { get; set; }

    /// <summary>When it was created.</summary>
    public DateTime Created { get; set; }

    /// <summary>When it expires; null = never.</summary>
    public DateTime? Expires { get; set; }

    /// <summary>The listing row of <paramref name="key"/>.</summary>
    public static AccountApiKey From(ApiKey key) => new()
    {
        Key = key.Key,
        Description = key.Description,
        Scopes = key.Scopes ?? Array.Empty<string>(),
        PackageGlobPattern = key.PackageGlobPattern,
        Created = key.Created,
        Expires = key.Expires,
    };
}
