using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Catalog.Database;
/// <summary>
/// Used when the configured database cannot hold the catalog.
/// </summary>
public class NullCatalogDatabase : ICatalogDatabase
{
    public bool IsSupported => false;

    public Task<DateTime?> GetLastCommitTimestampAsync(CancellationToken cancellationToken)
        => Task.FromResult<DateTime?>(null);

    public Task AddAsync(CatalogEntry entry, CancellationToken cancellationToken)
        => throw new NotSupportedException("The configured database does not support the catalog.");

    public Task<IReadOnlyList<DateTime>> GetCommitTimestampsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<DateTime>>(Array.Empty<DateTime>());

    public Task<IReadOnlyList<CatalogEntry>> GetEntriesAsync(int skip, int take, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<CatalogEntry>>(Array.Empty<CatalogEntry>());

    #pragma warning disable CS8619, CS8625
    public Task<CatalogEntry?> FindOrNullAsync(string commitId, CancellationToken cancellationToken)
        => Task.FromResult<CatalogEntry>(null);
    #pragma warning restore CS8619, CS8625
}