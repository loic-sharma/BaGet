using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Identity.Application.Services.Accounts;

/// <summary>
/// Creates, lists and revokes users' API keys. A key is shown once, when it is created;
/// only its hash is stored.
/// </summary>
public class ApiKeyService
{
    /// <summary>
    /// Marks a BaGet key, so a leaked one is recognisable by secret scanners.
    /// </summary>
    internal const string KeyPrefix = "bgt_";

    private readonly IContext _context;
    private readonly SystemTime _time;

    public ApiKeyService(IContext context, SystemTime time)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// Create a key and return it in plain text. It cannot be recovered afterwards.
    /// </summary>
    public Task<string> CreateAsync(
        string? userId,
        string? description,
        IReadOnlyList<string>? scopes,
        string packageGlobPattern,
        DateTime? expires,
        CancellationToken cancellationToken)
        => CreateAsync(userId, description, scopes, packageGlobPattern, expires, repositoryKeys: null, cancellationToken);

    /// <summary>
    /// Create a key limited to <paramref name="repositoryKeys"/> (<see cref="ApiKeyRepository"/>), or usable in every
    /// repository when null or empty, and return it in plain text. It cannot be recovered afterwards.
    /// </summary>
    /// <exception cref="ArgumentException">A repository key does not name an existing repository.</exception>
    public async Task<string> CreateAsync(
        string? userId,
        string? description,
        IReadOnlyList<string>? scopes,
        string packageGlobPattern,
        DateTime? expires,
        IReadOnlyCollection<int>? repositoryKeys,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(userId)) throw new ArgumentException("A user is required.", nameof(userId));

        var unknown = scopes?.FirstOrDefault(s => !ApiKeyScopes.Assignable.Contains(s, StringComparer.Ordinal));
        if (scopes == null || scopes.Count == 0 || unknown != null)
        {
            throw new ArgumentException($"Scopes must be some of: {string.Join(", ", ApiKeyScopes.Assignable)}.", nameof(scopes));
        }

        if (scopes.Contains(ApiKeyScopes.Admin, StringComparer.Ordinal)
            && !await _context.Users.AnyAsync(u => u.Id == userId && u.IsAdministrator, cancellationToken))
        {
            throw new ArgumentException($"The scope {ApiKeyScopes.Admin} is for administrators.", nameof(scopes));
        }

        var limit = repositoryKeys?.Distinct().ToList() ?? new List<int>();
        if (limit.Count > 0)
        {
            var known = await _context.Repositories!
                .Where(r => limit.Contains(r.Key))
                .Select(r => r.Key)
                .ToListAsync(cancellationToken);

            if (known.Count != limit.Count)
            {
                throw new ArgumentException("A chosen repository does not exist.", nameof(repositoryKeys));
            }
        }

        var key = KeyPrefix + Base64Url(RandomNumberGenerator.GetBytes(32));

        var entity = new ApiKey
        {
            UserId = userId,
            Description = description,
            HashedKey = Hash(key),
            Scopes = scopes.Distinct(StringComparer.Ordinal).ToArray(),
            PackageGlobPattern = string.IsNullOrWhiteSpace(packageGlobPattern) ? "*" : packageGlobPattern.Trim(),
            Created = _time.UtcNow,
            Expires = expires?.ToUniversalTime(),
        };
        _context.ApiKeys?.Add(entity);

        // Saved with the key in one transaction: a key never exists half-limited (which would mean unlimited).
        foreach (var repositoryKey in limit)
        {
            _context.ApiKeyRepositories!.Add(new ApiKeyRepository { RepositoryKey = repositoryKey, ApiKey = entity });
        }

        await _context.SaveChangesAsync(cancellationToken);

        return key;
    }

    public async Task<IReadOnlyList<ApiKey>> ListAsync(string? userId, CancellationToken cancellationToken)
    {
        #pragma warning disable CS8604
        return await _context.ApiKeys
            .AsNoTracking()
            .Where(k => k.UserId == userId)
            .OrderBy(k => k.Key)
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8604
    }

    /// <summary>
    /// The keys of the repositories <paramref name="apiKey"/> is limited to, or null when it may be used in every one.
    /// </summary>
    public async Task<IReadOnlyList<int>?> RepositoryKeysAsync(int apiKey, CancellationToken cancellationToken)
    {
        var keys = await _context.ApiKeyRepositories!
            .AsNoTracking()
            .Where(r => r.ApiKeyKey == apiKey)
            .Select(r => r.RepositoryKey)
            .ToListAsync(cancellationToken);

        return keys.Count == 0 ? null : keys;
    }

    /// <summary>
    /// The names of the repositories each of <paramref name="keys"/> is limited to, by key number; a key that may be
    /// used everywhere has no entry. Names are ordered.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> RepositoryNamesAsync(
        IEnumerable<ApiKey> keys,
        CancellationToken cancellationToken)
    {
        var numbers = keys.Select(k => k.Key).ToList();
        if (numbers.Count == 0) return new Dictionary<int, IReadOnlyList<string>>();

        var rows = await (
            from limit in _context.ApiKeyRepositories!.AsNoTracking()
            join repository in _context.Repositories!.AsNoTracking() on limit.RepositoryKey equals repository.Key
            where numbers.Contains(limit.ApiKeyKey)
            select new { limit.ApiKeyKey, repository.Name })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.ApiKeyKey)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(r => r.Name ?? string.Empty).OrderBy(n => n, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// Text for a key's repository limit: the names, or "all repositories" when it has none
    /// (<paramref name="names"/> null or empty).
    /// </summary>
    public static string DescribeRepositories(IReadOnlyList<string>? names)
        => names == null || names.Count == 0 ? "all repositories" : string.Join(", ", names);

    /// <summary>
    /// Revoke one of the user's keys. False if the user has no such key.
    /// </summary>
    public async Task<bool> RevokeAsync(string? userId, int key, CancellationToken cancellationToken)
    {
        #pragma warning disable CS8602
        var apiKey = await _context.ApiKeys?.FirstOrDefaultAsync(k => k.Key == key && k.UserId == userId, cancellationToken);
        #pragma warning restore CS8602
        if (apiKey == null) return false;

        _context.ApiKeys?.Remove(apiKey);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// The stored key that matches, if it exists and has not expired.
    /// </summary>
    public async Task<ApiKey?> FindValidOrNullAsync(string? key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(key) || !key.StartsWith(KeyPrefix, StringComparison.Ordinal)) return null;

        var hash = Hash(key);
        #pragma warning disable CS8604
        var apiKey = await _context.ApiKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.HashedKey == hash, cancellationToken);
        #pragma warning restore CS8604

        if (apiKey == null) return null;
        if (apiKey.Expires.HasValue && DateTime.SpecifyKind(apiKey.Expires.Value, DateTimeKind.Utc) <= _time.UtcNow) return null;

        return apiKey;
    }

    /// <summary>
    /// A key is 256 random bits, so a fast unsalted hash is enough: there is nothing
    /// to brute force, and the lookup by hash must be exact.
    /// </summary>
    internal static string Hash(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}