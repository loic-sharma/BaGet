using System;
using System.Collections.Generic;
using BaGet.Core;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Identity.Application.Models.Account;

/// <summary>
/// The API keys screen: the new-key form's fields it repeats and the user's keys. A request binds <see cref="ApiKeyForm"/>, never this type,
/// so the outputs cannot be set from outside.
/// </summary>
public sealed class ApiKeysViewModel : ApiKeyForm
{
    /// <summary>
    /// The repositories offered in the picker: those the user can read. Empty (picker hidden) where the server holds
    /// the default repository only.
    /// </summary>
    public IReadOnlyList<RepositoryInfo> Repositories { get; set; } = Array.Empty<RepositoryInfo>();

    /// <summary>The user's keys.</summary>
    public IReadOnlyList<AccountApiKey> Keys { get; set; } = Array.Empty<AccountApiKey>();

    /// <summary>The names of the repositories each key (by key id) is limited to.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<string>> KeyRepositories { get; set; } = new Dictionary<int, IReadOnlyList<string>>();

    /// <summary>The key just created. Shown once: only its hash is kept.</summary>
    public string? CreatedKey { get; set; }

    /// <summary>
    /// The scopes the new-key form offers: the package scopes, plus <see cref="ApiKeyScopes.Admin"/> (the REST API under
    /// <c>/api/v1</c>) for an administrator.
    /// </summary>
    public IReadOnlyList<string> AllScopes { get; set; } = ApiKeyScopes.All;

    /// <summary>Where the key may be used: the repositories it is limited to, or "all repositories".</summary>
    public string DescribeRepositories(AccountApiKey key)
        => ApiKeyService.DescribeRepositories(KeyRepositories.GetValueOrDefault(key.Key));

    /// <summary>What the key can do and on which package IDs.</summary>
    public static string Describe(AccountApiKey key)
        => string.Join(", ", key.Scopes) + " on " + key.PackageGlobPattern;
}
