using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Catalog.Database;
/// <summary>
/// The catalog log in the relational database, beside the packages.
/// </summary>
public class CatalogDatabase : ICatalogDatabase
{
    private readonly IContext _context;

    public CatalogDatabase(IContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public bool IsSupported => true;

    public async Task<DateTime?> GetLastCommitTimestampAsync(CancellationToken cancellationToken)
    {
        // Entries are added in commit order, so the highest key is the newest.
        #pragma warning disable CS8604
        var last = await _context.CatalogLeafs
            .OrderByDescending(e => e.Key)
            .Select(e => (DateTime?)e.CommitTimestamp)
            .FirstOrDefaultAsync(cancellationToken);
        #pragma warning restore CS8604

        return last.HasValue ? AsUtc(last.Value) : (DateTime?)null;
    }

    public async Task AddAsync(CatalogEntry entry, CancellationToken cancellationToken)
    {
        _context.CatalogLeafs?.Add(entry);

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DateTime>> GetCommitTimestampsAsync(CancellationToken cancellationToken)
    {
        #pragma warning disable CS8604
        var timestamps = await _context.CatalogLeafs
            .OrderBy(e => e.Key)
            .Select(e => e.CommitTimestamp)
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8604

        return timestamps.Select(AsUtc).ToList();
    }

    public async Task<(int Count, IReadOnlyList<DateTime> PageEnds)> GetPageEndsAsync(int pageSize, CancellationToken cancellationToken)
    {
        #pragma warning disable CS8604
        var count = await _context.CatalogLeafs.CountAsync(cancellationToken);

        var ends = new List<DateTime>();
        var pages = (count + pageSize - 1) / pageSize;
        var lastKey = 0;
        for (var page = 0; page < pages; page++)
        {
            // Keyset paging: each page costs its own size, not the number of entries before it.
            var isLast = page == pages - 1;
            var entries = _context.CatalogLeafs.AsNoTracking();
            if (isLast)
            {
                var latest = await entries.OrderByDescending(e => e.Key).Select(e => e.CommitTimestamp).FirstAsync(cancellationToken);
                ends.Add(AsUtc(latest));
                break;
            }

            var end = await entries
                .Where(e => e.Key > lastKey)
                .OrderBy(e => e.Key)
                .Skip(pageSize - 1)
                .Select(e => new { e.Key, e.CommitTimestamp })
                .FirstAsync(cancellationToken);

            lastKey = end.Key;
            ends.Add(AsUtc(end.CommitTimestamp));
        }
        #pragma warning restore CS8604

        return (count, ends);
    }

    public async Task<IReadOnlyList<CatalogEntry>> GetEntriesAsync(int skip, int take, CancellationToken cancellationToken)
    {
        #pragma warning disable CS8604
        var entries = await _context.CatalogLeafs
            .AsNoTracking()
            .OrderBy(e => e.Key)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8604

        foreach (var entry in entries)
        {
            entry.CommitTimestamp = AsUtc(entry.CommitTimestamp);
        }

        return entries;
    }

    public async Task<CatalogEntry?> FindOrNullAsync(string commitId, CancellationToken cancellationToken)
    {
        #pragma warning disable CS8604
        var entry = await _context.CatalogLeafs
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.CommitId == commitId, cancellationToken);
        #pragma warning restore CS8604

        if (entry != null)
        {
            entry.CommitTimestamp = AsUtc(entry.CommitTimestamp);
        }

        return entry;
    }

    // Every timestamp is written as UTC, but some providers, such as SQLite, read it
    // back without a kind.
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
