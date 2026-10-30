using System;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Core;

/// <summary>
/// Re-indexes a package ID in the search index (<see cref="ISearchIndexer"/>) whenever one of its versions is unlisted, relisted,
/// has its metadata changed or is deleted: <see cref="PackageIndexingService"/> indexes on push only, so an external index (Azure
/// Search) would otherwise keep a deleted or unlisted version, and miss a relisted one, until the next push of the same ID.
/// A no-op with the default <see cref="NullSearchIndexer"/> (the database searches itself).
/// </summary>
public sealed class SearchIndexChangeHandler : IPackageChangeHandler
{
    private readonly ISearchIndexer _indexer;

    public SearchIndexChangeHandler(ISearchIndexer indexer)
    {
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
    }

    /// <inheritdoc/>
    public Task OnPackageChangedAsync(string? id, NuGetVersion version, CancellationToken cancellationToken) => ReindexAsync(id, cancellationToken);

    /// <inheritdoc/>
    public Task OnPackageDeletedAsync(string? id, NuGetVersion version, CancellationToken cancellationToken) => ReindexAsync(id, cancellationToken);

    private Task ReindexAsync(string? id, CancellationToken cancellationToken)
        => string.IsNullOrEmpty(id) || _indexer is NullSearchIndexer
            ? Task.CompletedTask
            : _indexer.IndexAsync(new Package { Id = id }, cancellationToken);
}
