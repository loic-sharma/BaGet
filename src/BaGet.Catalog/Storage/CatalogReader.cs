using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Protocol.Models;
using BaGet.Catalog.Database;

namespace BaGet.Catalog.Storage;

/// <summary>
/// Builds the catalog documents. The index and pages are built from the database on
/// read, so their URLs always match the host the client used; the leaves are read from
/// storage, since a deleted package's details exist nowhere else.
/// </summary>
public class CatalogReader
{
    /// <summary>
    /// nuget.org uses 550. Any size is valid; a client follows the index.
    /// </summary>
    internal const int PageSize = 500;

    private readonly ICatalogDatabase _catalog;
    private readonly IStorageService _storage;
    private readonly IUrlGenerator _url;
    private readonly IRepositoryContext? _repository;

    public CatalogReader(ICatalogDatabase catalog, IStorageService storage, IUrlGenerator url, IRepositoryContext? repository = null)
    {
        _repository = repository;
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _url = url ?? throw new ArgumentNullException(nameof(url));
    }

    public bool IsSupported => _catalog.IsSupported;

    public async Task<CatalogIndex> GetIndexAsync(CancellationToken cancellationToken)
    {
        var (total, ends) = await _catalog.GetPageEndsAsync(PageSize, cancellationToken);

        return new CatalogIndex
        {
            CommitTimestamp = total == 0 ? DateTimeOffset.UnixEpoch : Utc(ends[^1]),
            Count = PageCount(total),
            Items = Enumerable.Range(0, PageCount(total))
                .Select(page => new CatalogPageItem
                {
                    CatalogPageUrl = _url.GetCatalogPageUrl(page),
                    CommitTimestamp = Utc(ends[page]),
                    Count = Math.Min(PageSize, total - page * PageSize),
                })
                .ToList(),
        };
    }

    /// <summary>
    /// One page, or null if the page does not exist.
    /// </summary>
    public async Task<CatalogPage?> GetPageOrNullAsync(int pageIndex, CancellationToken cancellationToken)
    {
        if (pageIndex < 0) return null;

        var entries = await _catalog.GetEntriesAsync(pageIndex * PageSize, PageSize, cancellationToken);
        if (entries.Count == 0) return null;

        return new CatalogPage
        {
            CommitTimestamp = Utc(entries[^1].CommitTimestamp),
            Count = entries.Count,
            CatalogIndexUrl = _url.GetCatalogIndexUrl(),
            Items = entries
                .Select(e => new CatalogLeafItem
                {
                    CatalogLeafUrl = _url.GetCatalogLeafUrl(e.CommitId),
                    Type = "nuget:" + e.Type,
                    CommitTimestamp = Utc(e.CommitTimestamp),
                    PackageId = e.PackageId,
                    PackageVersion = e.PackageVersion,
                })
                .ToList(),
        };
    }

    /// <summary>
    /// One leaf document as JSON, or null if no such leaf exists.
    /// </summary>
    public async Task<string?> GetLeafOrNullAsync(string commitId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(commitId, "D", out _)) return null;

        var entry = await _catalog.FindOrNullAsync(commitId, cancellationToken);
        if (entry == null) return null;

        JsonNode? leaf;
        using (var stream = await _storage.GetAsync(CatalogWriter.LeafPath(_repository?.Current ?? RepositoryInfo.Default, entry.CommitId), cancellationToken))
        {
            leaf = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken);
        }

        // The leaf's own URL is not stored, so it follows the host like every other URL.
        leaf?["@id"] = _url.GetCatalogLeafUrl(entry.CommitId);

        return leaf?.ToJsonString();
    }

    internal static int PageCount(int entries) => (entries + PageSize - 1) / PageSize;

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}