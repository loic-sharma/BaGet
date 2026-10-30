using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using BaGet.Catalog.Database;
using BaGet.Catalog.Storage;

namespace BaGet.Catalog.Rebuild;

/// <summary>
/// Reconciles the catalog with the packages: appends a details entry for every package
/// version whose latest entry is missing or a delete, and a delete entry for every
/// version whose latest entry is details but which no longer exists. This backfills
/// packages pushed before the catalog existed, and repairs a commit that failed.
/// </summary>
/// <remarks>
/// The log is append-only, so nothing is rewritten. A change whose commit failed while
/// the version kept existing, such as an unlist, cannot be detected and is not repaired.
/// </remarks>
public class CatalogRebuilder
{
    private readonly ICatalogDatabase _catalog;
    private readonly IContext _context;
    private readonly CatalogWriter _writer;
    private readonly ILogger<CatalogRebuilder> _logger;

    public CatalogRebuilder(
        ICatalogDatabase catalog,
        IContext context,
        CatalogWriter writer,
        ILogger<CatalogRebuilder> logger)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<(int Added, int Deleted)> RebuildAsync(CancellationToken cancellationToken)
    {
        if (!_catalog.IsSupported)
        {
            _logger.LogWarning("The configured database does not support the catalog");
            return (0, 0);
        }

        var latest = new Dictionary<(string, string), CatalogEntry>();
        for (var skip = 0; ; skip += CatalogReader.PageSize)
        {
            var entries = await _catalog.GetEntriesAsync(skip, CatalogReader.PageSize, cancellationToken);
            foreach (var entry in entries)
            {
                latest[Identity(entry.PackageId, entry.PackageVersion)] = entry;
            }

            if (entries.Count < CatalogReader.PageSize) break;
        }

        #pragma warning disable CS8604
        var packages = await _context.Packages
            .AsNoTracking()
            .Select(p => new { p.Id, p.NormalizedVersionString })
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8604

        var existing = new HashSet<(string, string)>();
        var added = 0;
        foreach (var package in packages.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
        {
            var identity = Identity(package.Id, package.NormalizedVersionString);
            existing.Add(identity);

            if (latest.TryGetValue(identity, out var entry) && entry.Type == CatalogEntry.PackageDetailsType)
            {
                continue;
            }

            #pragma warning disable CS8604
            await _writer.OnPackageChangedAsync(
                package.Id,
                NuGetVersion.Parse(package.NormalizedVersionString),
                cancellationToken);
            #pragma warning restore CS8604
            added++;
        }

        var deleted = 0;
        foreach (var pair in latest.Where(l => l.Value.Type == CatalogEntry.PackageDetailsType && !existing.Contains(l.Key)))
        {
            #pragma warning disable CS8604
            await _writer.OnPackageDeletedAsync(
                pair.Value.PackageId,
                NuGetVersion.Parse(pair.Value.PackageVersion),
                cancellationToken);
            #pragma warning restore CS8604
            deleted++;
        }

        _logger.LogInformation(
            "Catalog rebuilt: {Added} details entries and {Deleted} delete entries appended",
            added,
            deleted);

        return (added, deleted);
    }

    // Package IDs are case insensitive.
    #pragma warning disable CS8604, CS8619
    private static (string, string) Identity(string? id, string? version)
        => (id?.ToLowerInvariant(), NuGetVersion.Parse(version).ToNormalizedString().ToLowerInvariant());
    #pragma warning restore CS8604, CS8619
}