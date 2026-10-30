using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BaGet.Identity.Application.Services.Accounts;

/// <summary>
/// Works out the Gravatar address of an account. Each address is a request to gravatar.com
/// that carries a hash of the account's email, so <see cref="SiteOptions.UseGravatar"/> turns
/// them all off; callers then fall back to an initial-letter placeholder.
/// </summary>
public class AvatarService
{
    private readonly IContext _context;
    private readonly IOptionsSnapshot<SiteOptions> _site;

    public AvatarService(IContext context, IOptionsSnapshot<SiteOptions> site)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _site = site ?? throw new ArgumentNullException(nameof(site));
    }

    public bool Enabled => _site.Value.UseGravatar;

    /// <summary>
    /// The avatar address by account name (case-insensitive), or an empty map when Gravatar
    /// is off. A user with an email is identified by it; an organization, or a user without
    /// one, by the lowercased name.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetUrlsAsync(
        IEnumerable<string?> names,
        int size,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Enabled) return result;

        var distinct = names.Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count == 0) return result;

        var lowered = distinct.Select(n => n?.ToLowerInvariant()).ToList();
        #pragma warning disable CS8602
        var users = await _context.Users
            .AsNoTracking()
            .Where(u => lowered.Contains(u.UserName.ToLower()))
            .Select(u => new { u.UserName, u.Email })
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8602
        #pragma warning disable CS8621, CS8714
        var emails = users.ToDictionary(u => u.UserName, u => u.Email, StringComparer.OrdinalIgnoreCase);
        #pragma warning restore CS8621, CS8714

        foreach (var name in distinct)
        {
            emails.TryGetValue(name, out var email);
            #pragma warning disable CS8604
            result[name] = GetUrl(string.IsNullOrWhiteSpace(email) ? name : email, size);
            #pragma warning restore CS8604
        }

        return result;
    }

    /// <summary>
    /// The Gravatar address of a key: the MD5 of the trimmed, lowercased email (or name).
    /// The picture is requested at twice <paramref name="size"/> for high-density screens.
    /// </summary>
    public static string GetUrl(string key, int size)
    {
        var normalized = key.Trim().ToLowerInvariant();
        var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();

        return $"https://www.gravatar.com/avatar/{hash}?s={size * 2}&d=retro";
    }
}
