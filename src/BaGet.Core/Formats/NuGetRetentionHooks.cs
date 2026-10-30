using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NuGet.Versioning;

namespace BaGet.Core;

/// <summary>
/// NuGet's <see cref="IFormatRetentionHooks"/>: versions are the repository's package rows, deleted for good through
/// <see cref="IPackageDeletionService.TryHardDeletePackageAsync"/> whatever <c>PackageDeletionBehavior</c> says (a retention policy
/// is cleanup, not the protocol's DELETE). Download times come from <see cref="ArtifactUsageMap"/>.
/// </summary>
public sealed class NuGetRetentionHooks : IFormatRetentionHooks
{
    private readonly IContext _context;
    private readonly IPackageDeletionService _deletion;
    private readonly IPackageStorageService _storage;

    public NuGetRetentionHooks(IContext context, IPackageDeletionService deletion, IPackageStorageService storage)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _deletion = deletion ?? throw new ArgumentNullException(nameof(deletion));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    /// <inheritdoc/>
    public RepositoryFormat Format => RepositoryFormat.NuGet;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FormatVersion>> ListVersionsAsync(CancellationToken cancellationToken)
    {
        var usage = await ArtifactUsageMap.LoadAsync(_context, cancellationToken).ConfigureAwait(false);
        var rows = await _context.Packages!.AsNoTracking()
            .Select(p => new { p.Id, p.NormalizedVersionString, p.Published, p.IsPrerelease })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // The size of a package is in the store, not the row: GetSizeAsync reads it when a cache limit needs it.
        return rows
            .Select(r => new FormatVersion(r.Id!, r.NormalizedVersionString!, DateTime.SpecifyKind(r.Published, DateTimeKind.Utc), usage.Get(r.Id!, r.NormalizedVersionString!), null, r.IsPrerelease))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task DeleteVersionAsync(string name, string version, CancellationToken cancellationToken)
        => await _deletion.TryHardDeletePackageAsync(name, NuGetVersion.Parse(version), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<long?> GetSizeAsync(string name, string version, CancellationToken cancellationToken)
    {
        using var stream = await _storage.GetPackageStreamAsync(name, NuGetVersion.Parse(version), cancellationToken).ConfigureAwait(false);
        if (stream == null) return null;
        if (stream.CanSeek) return stream.Length;

        // A store that cannot say (object storage behind a forward-only stream): count the bytes.
        long total = 0;
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0) total += read;

        return total;
    }

    /// <inheritdoc/>
    public (string Name, string Version)? Canonical(string name, string? version)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (string.IsNullOrWhiteSpace(version)) return (name.Trim(), string.Empty);

        return NuGetVersion.TryParse(version.Trim(), out var parsed) ? (name.Trim(), parsed.ToNormalizedString().ToLowerInvariant()) : null;
    }
}
