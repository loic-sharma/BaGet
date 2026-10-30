using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core;

/// <summary>
/// Maven's <see cref="IFormatRetentionHooks"/>: a version is one <see cref="MavenVersion"/> of a <c>group:artifact</c> with all its
/// files (a snapshot with its timestamped builds), a snapshot being the prerelease. Deleting is
/// <see cref="MavenRepositoryService.PurgeAsync"/>: rows and files together, the artifact going with its last version.
/// </summary>
public sealed class MavenRetentionHooks : IFormatRetentionHooks
{
    private readonly IContext _context;
    private readonly MavenRepositoryService _repository;

    public MavenRetentionHooks(IContext context, MavenRepositoryService repository)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <inheritdoc/>
    public RepositoryFormat Format => RepositoryFormat.Maven;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FormatVersion>> ListVersionsAsync(CancellationToken cancellationToken)
    {
        var usage = await ArtifactUsageMap.LoadAsync(_context, cancellationToken).ConfigureAwait(false);
        var rows = await (from v in _context.MavenVersions!.AsNoTracking()
                          join a in _context.MavenArtifacts!.AsNoTracking() on v.ArtifactKey equals a.Key
                          select new { v.Key, a.GroupId, a.ArtifactId, v.Version, v.Created, v.IsSnapshot }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var sizes = (await (from f in _context.MavenFiles!.AsNoTracking()
                            join v in _context.MavenVersions!.AsNoTracking() on f.VersionKey equals v.Key
                            join a in _context.MavenArtifacts!.AsNoTracking() on v.ArtifactKey equals a.Key
                            group f.Size by f.VersionKey into g
                            select new { VersionKey = g.Key, Bytes = g.Sum() }).ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(x => x.VersionKey, x => x.Bytes);

        return rows
            .Select(r =>
            {
                var name = MavenNames.ArtifactName(r.GroupId!, r.ArtifactId!);

                return new FormatVersion(name, r.Version!, DateTime.SpecifyKind(r.Created, DateTimeKind.Utc), usage.Get(name, r.Version!), sizes.TryGetValue(r.Key, out var bytes) ? bytes : 0, r.IsSnapshot);
            })
            .ToList();
    }

    /// <inheritdoc/>
    public async Task DeleteVersionAsync(string name, string version, CancellationToken cancellationToken)
        => await _repository.PurgeAsync(name, version, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public (string Name, string Version)? Canonical(string name, string? version)
    {
        if (!MavenNames.TryParseArtifactName(name?.Trim(), out var group, out var artifact)) return null;
        if (string.IsNullOrWhiteSpace(version)) return (MavenNames.ArtifactName(group, artifact), string.Empty);

        return MavenNames.IsValidVersion(version.Trim()) ? (MavenNames.ArtifactName(group, artifact), version.Trim()) : null;
    }
}
