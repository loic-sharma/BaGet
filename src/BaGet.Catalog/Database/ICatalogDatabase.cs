using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;

namespace BaGet.Catalog.Database;

/// <summary>
/// Where the catalog log is kept.
/// </summary>
public interface ICatalogDatabase
{
    /// <summary>
    /// False when the configured database cannot hold the catalog, such as Azure Table
    /// storage. The catalog is then neither written nor advertised.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// The newest entry's commit timestamp, or null if the log is empty.
    /// </summary>
    Task<DateTime?> GetLastCommitTimestampAsync(CancellationToken cancellationToken);

    Task AddAsync(CatalogEntry entry, CancellationToken cancellationToken);

    /// <summary>
    /// Every entry's commit timestamp, in commit order. Used to lay out the pages.
    /// </summary>
    Task<IReadOnlyList<DateTime>> GetCommitTimestampsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The number of entries and the commit timestamp of the last entry of each full or partial page of
    /// <paramref name="pageSize"/> entries: all the index needs, without reading every timestamp.
    /// </summary>
    /// <remarks>The default derives it from <see cref="GetCommitTimestampsAsync"/>.</remarks>
    async Task<(int Count, IReadOnlyList<DateTime> PageEnds)> GetPageEndsAsync(int pageSize, CancellationToken cancellationToken)
    {
        var all = await GetCommitTimestampsAsync(cancellationToken);
        var ends = new List<DateTime>();
        for (var end = pageSize - 1; end < all.Count + pageSize - 1; end += pageSize)
        {
            ends.Add(all[Math.Min(end, all.Count - 1)]);
        }

        return (all.Count, ends);
    }

    /// <summary>
    /// The entries in commit order, skipping <paramref name="skip"/>.
    /// </summary>
    Task<IReadOnlyList<CatalogEntry>> GetEntriesAsync(int skip, int take, CancellationToken cancellationToken);

    Task<CatalogEntry?> FindOrNullAsync(string commitId, CancellationToken cancellationToken);
}