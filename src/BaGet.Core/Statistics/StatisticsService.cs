using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core;

public class FeedStatistics
{
    public int PackageIds { get; set; }

    public int PackageVersions { get; set; }

    public long Downloads { get; set; }

    /// <summary>
    /// The most downloaded package IDs, all versions together.
    /// </summary>
    public IReadOnlyList<PackageDownloads>? TopPackages { get; set; }

    /// <summary>
    /// The most recently published versions.
    /// </summary>
    public IReadOnlyList<PackageDownloads>? RecentVersions { get; set; }
}

public class PackageDownloads
{
    public string? Id { get; set; }

    /// <summary>
    /// Null when the downloads are a whole package ID's.
    /// </summary>
    public string? Version { get; set; }

    public long Downloads { get; set; }

    public DateTime? Published { get; set; }
}

/// <summary>
/// Feed-wide download statistics, from the per-version counts the database keeps.
/// There is no per-day history: a count only ever goes up.
/// </summary>
public class StatisticsService
{
    private const int ListSize = 10;

    private readonly IContext _context;

    public StatisticsService(IContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// False when the database cannot be queried for aggregates, such as Azure Table storage.
    /// </summary>
    public bool IsSupported => !(_context is NullContext);

    public async Task<FeedStatistics> GetAsync(CancellationToken cancellationToken)
    {
        var listed = _context.Packages?.AsNoTracking().Where(p => p.Listed);

        #pragma warning disable CS8604
        var top = await listed
            .GroupBy(p => p.Id)
            .Select(g => new PackageDownloads { Id = g.Key, Downloads = g.Sum(p => p.Downloads) })
            .OrderByDescending(p => p.Downloads)
            .ThenBy(p => p.Id)
            .Take(ListSize)
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8604

        var recent = await listed
            .OrderByDescending(p => p.Published)
            .ThenByDescending(p => p.Key)
            .Take(ListSize)
            .Select(p => new PackageDownloads
            {
                Id = p.Id,
                Version = p.OriginalVersionString ?? p.NormalizedVersionString,
                Downloads = p.Downloads,
                Published = p.Published,
            })
            .ToListAsync(cancellationToken);

        return new FeedStatistics
        {
            PackageIds = await listed.Select(p => p.Id).Distinct().CountAsync(cancellationToken),
            PackageVersions = await listed.CountAsync(cancellationToken),
            Downloads = await listed.SumAsync(p => p.Downloads, cancellationToken),
            TopPackages = top,
            RecentVersions = recent,
        };
    }

    /// <summary>
    /// When each package ID was last published to, for the IDs that have a listed version.
    /// Empty when <see cref="IsSupported"/> is false. Only the versions a search with the same
    /// prerelease and SemVer 2 filters would match are considered, so the date agrees with the
    /// "last updated" order.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, DateTime>> GetLastUpdatedAsync(
        IReadOnlyCollection<string?>? packageIds,
        CancellationToken cancellationToken,
        bool includePrerelease = true,
        bool includeSemVer2 = true)
    {
        var result = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        if (!IsSupported || packageIds == null || packageIds.Count == 0) return result;

        var lowered = packageIds.Select(id => id?.ToLower()).ToList();
        var packages = _context.Packages?.AsNoTracking();
        #pragma warning disable CS8604
        if (!includePrerelease) packages = packages.Where(p => !p.IsPrerelease);
        if (!includeSemVer2) packages = packages.Where(p => p.SemVerLevel != SemVerLevel.SemVer2);
        #pragma warning restore CS8604

        #pragma warning disable CS8602, CS8604
        var rows = await packages
            .Where(p => p.Listed && lowered.Contains(p.Id.ToLower()))
            .GroupBy(p => p.Id)
            .Select(g => new { Id = g.Key, Published = g.Max(p => p.Published) })
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8602, CS8604

        #pragma warning disable CS8604
        foreach (var row in rows) result[row.Id] = row.Published;
        #pragma warning restore CS8604

        return result;
    }
}
