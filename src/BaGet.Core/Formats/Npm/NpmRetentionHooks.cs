using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core;

/// <summary>
/// npm's <see cref="IFormatRetentionHooks"/>: a version is one <see cref="NpmVersion"/> (with its tarball). Deleting is
/// <see cref="NpmRegistryService.PurgeAsync"/>, the administrator's unauthorised unpublish: the tags that pointed at the version go
/// and <c>latest</c> moves to the highest version left; the last version takes the package with it.
/// </summary>
public sealed class NpmRetentionHooks : IFormatRetentionHooks
{
    private readonly IContext _context;
    private readonly NpmRegistryService _registry;

    public NpmRetentionHooks(IContext context, NpmRegistryService registry)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <inheritdoc/>
    public RepositoryFormat Format => RepositoryFormat.Npm;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FormatVersion>> ListVersionsAsync(CancellationToken cancellationToken)
    {
        var usage = await ArtifactUsageMap.LoadAsync(_context, cancellationToken).ConfigureAwait(false);
        var rows = await (from v in _context.NpmVersions!.AsNoTracking()
                          join p in _context.NpmPackages!.AsNoTracking() on v.PackageKey equals p.Key
                          select new { p.Name, v.Version, v.Published, v.TarballSize }).ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows
            .Select(r => new FormatVersion(r.Name!, r.Version!, DateTime.SpecifyKind(r.Published, DateTimeKind.Utc), usage.Get(r.Name!, r.Version!), r.TarballSize, NpmNames.IsPrerelease(r.Version!)))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task DeleteVersionAsync(string name, string version, CancellationToken cancellationToken)
        => await _registry.PurgeAsync(name, version, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public (string Name, string Version)? Canonical(string name, string? version)
    {
        // npm names are lower case (the registry refuses new upper-case ones), so a typed name is folded before it is checked.
        if (!NpmNames.TryParse(name?.Trim().ToLowerInvariant(), out var normalized, out _)) return null;
        if (string.IsNullOrWhiteSpace(version)) return (normalized, string.Empty);

        return NpmNames.TryParseVersion(version.Trim(), out var v) ? (normalized, v) : null;
    }
}
