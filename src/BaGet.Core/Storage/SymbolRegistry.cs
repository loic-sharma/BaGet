using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NuGet.Versioning;

namespace BaGet.Core;

/// <summary>
/// <see cref="ISymbolRegistry"/> over the database (<see cref="PackageSymbol"/>) and the symbol storage.
/// Rows are repository-scoped through the context's query filter, so each repository sees only its own symbols.
/// </summary>
public class SymbolRegistry : ISymbolRegistry
{
    private readonly IContext _context;
    private readonly ISymbolStorageService _storage;
    private readonly SystemTime _time;

    public SymbolRegistry(IContext context, ISymbolStorageService storage, SystemTime time)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public bool IsSupported => _context is not NullContext;

    public async Task RecordAsync(
        string packageId,
        NuGetVersion version,
        IEnumerable<SymbolReference> symbols,
        CancellationToken cancellationToken)
    {
        if (!IsSupported) return;

        var id = packageId.ToLowerInvariant();
        var normalized = version.ToNormalizedString().ToLowerInvariant();
        var existing = (await ListRowsAsync(id, normalized, asNoTracking: true, cancellationToken))
            .Select(r => (r.FileName, r.SymbolKey))
            .ToHashSet();

        foreach (var symbol in symbols)
        {
            var file = symbol.FileName.ToLowerInvariant();
            var key = symbol.Key.ToLowerInvariant();
            if (!existing.Add((file, key))) continue;

            _context.PackageSymbols!.Add(new PackageSymbol
            {
                PackageId = id,
                PackageVersion = normalized,
                FileName = file,
                SymbolKey = key,
                Created = _time.UtcNow,
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SymbolReference>> ListAsync(
        string packageId,
        NuGetVersion version,
        CancellationToken cancellationToken)
    {
        if (!IsSupported) return Array.Empty<SymbolReference>();

        var rows = await ListRowsAsync(
            packageId.ToLowerInvariant(),
            version.ToNormalizedString().ToLowerInvariant(),
            asNoTracking: true,
            cancellationToken);

        return rows.Select(r => new SymbolReference(r.FileName!, r.SymbolKey!)).ToList();
    }

    public async Task<int> DeleteAsync(string packageId, NuGetVersion version, CancellationToken cancellationToken)
    {
        if (!IsSupported) return 0;

        var rows = await ListRowsAsync(
            packageId.ToLowerInvariant(),
            version.ToNormalizedString().ToLowerInvariant(),
            asNoTracking: false,
            cancellationToken);
        if (rows.Count == 0) return 0;

        // The rows go first: a refused write (read-only gate) must leave the content in place.
        _context.PackageSymbols!.RemoveRange(rows);
        await _context.SaveChangesAsync(cancellationToken);

        // The same PDB can come from several versions (an unchanged assembly repackaged). Its content goes
        // only when no other version of this repository still records it.
        foreach (var row in rows)
        {
            var file = row.FileName!;
            var key = row.SymbolKey!;
            var stillUsed = await _context.PackageSymbols!
                .AnyAsync(s => s.FileName == file && s.SymbolKey == key, cancellationToken);
            if (!stillUsed)
            {
                await _storage.DeletePortablePdbContentAsync(file, key, cancellationToken);
            }
        }

        return rows.Count;
    }

    private async Task<List<PackageSymbol>> ListRowsAsync(string id, string version, bool asNoTracking, CancellationToken cancellationToken)
    {
        var query = _context.PackageSymbols!.Where(s => s.PackageId == id && s.PackageVersion == version);
        if (asNoTracking) query = query.AsNoTracking();

        return await query.OrderBy(s => s.FileName).ThenBy(s => s.SymbolKey).ToListAsync(cancellationToken);
    }
}
