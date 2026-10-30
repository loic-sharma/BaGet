using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core;

/// <summary>
/// What a Maven version is made of for scanning: itself and the POM's compile and runtime dependencies that state a literal
/// version (<c>1.2.3</c>, or the lower bound of a range such as <c>[1.2,2.0)</c>); versions that are properties
/// (<c>${spring.version}</c>), managed elsewhere or empty are skipped.
/// </summary>
public static class MavenScanComponents
{
    /// <summary>The OSV ecosystem name.</summary>
    public const string Ecosystem = "Maven";

    /// <summary>The artifact and the dependencies of <paramref name="pom"/> (null: just the artifact).</summary>
    public static IReadOnlyList<ScanComponent> Of(string groupId, string artifactId, string version, MavenPomInfo? pom)
    {
        var result = new List<ScanComponent> { new(Ecosystem, MavenNames.ArtifactName(groupId, artifactId), version) };
        foreach (var dependency in pom?.Dependencies ?? Array.Empty<MavenPomDependency>())
        {
            if (dependency.Optional) continue;
            if (dependency.Scope != null && dependency.Scope != "compile" && dependency.Scope != "runtime") continue;

            var literal = LiteralVersion(dependency.Version);
            if (literal == null) continue;

            var component = new ScanComponent(Ecosystem, MavenNames.ArtifactName(dependency.GroupId, dependency.ArtifactId), literal);
            if (!result.Contains(component)) result.Add(component);
        }

        return result;
    }

    /// <summary>The version a requirement names: a plain version, or the lower bound of a range; null when there is none.</summary>
    internal static string? LiteralVersion(string? requirement)
    {
        var value = requirement?.Trim();
        if (string.IsNullOrEmpty(value) || value.Contains("${", StringComparison.Ordinal)) return null;

        if (value[0] == '[' || value[0] == '(')
        {
            var inner = value.Trim('[', ']', '(', ')');
            var lower = inner.Split(',')[0].Trim();
            return lower.Length == 0 ? null : lower;
        }

        return value;
    }
}

/// <summary>Maven's <see cref="IFormatScanner"/>: the stored versions with the dependencies of their POM against OSV.</summary>
public sealed class MavenFormatScanner : IFormatScanner
{
    private readonly IContext _context;
    private readonly IStorageService _storage;
    private readonly IRepositoryContext _repository;

    public MavenFormatScanner(IContext context, IStorageService storage, IRepositoryContext repository)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <inheritdoc/>
    public RepositoryFormat Format => RepositoryFormat.Maven;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ScanComponent>> ListComponentsAsync(string name, string version, CancellationToken cancellationToken)
    {
        if (_context is NullContext || !MavenNames.TryParseArtifactName(name, out var groupId, out var artifactId)) return Array.Empty<ScanComponent>();

        var versionKey = await (from v in _context.MavenVersions!.AsNoTracking()
                                join a in _context.MavenArtifacts!.AsNoTracking() on v.ArtifactKey equals a.Key
                                where a.GroupId == groupId && a.ArtifactId == artifactId && v.Version == version
                                select (int?)v.Key).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (versionKey == null) return Array.Empty<ScanComponent>();

        MavenPomInfo? pom = null;
        var pomRow = await _context.MavenFiles!.AsNoTracking()
            .Where(f => f.VersionKey == versionKey && f.Extension == "pom" && f.Classifier == null)
            .OrderByDescending(f => f.SnapshotTimestamp)
            .ThenByDescending(f => f.SnapshotBuild)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (pomRow != null && pomRow.Size <= MavenPom.MaxBytes)
        {
            try
            {
                using var stream = await _storage.GetAsync(RepositoryStorage.Scope(_repository.Current, MavenNames.StoragePath(groupId, artifactId, version, pomRow.FileName!)), cancellationToken).ConfigureAwait(false);
                pom = MavenPom.TryParse(stream);
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                // A POM missing from storage leaves the artifact itself to scan.
            }
        }

        return MavenScanComponents.Of(groupId, artifactId, version, pom);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ScanArtifact>> ListArtifactsAsync(CancellationToken cancellationToken)
    {
        if (_context is NullContext) return Array.Empty<ScanArtifact>();

        var rows = await _context.MavenVersions!
            .AsNoTracking()
            .Join(_context.MavenArtifacts!, v => v.ArtifactKey, a => a.Key, (v, a) => new { a.GroupId, a.ArtifactId, v.Version })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(r => new ScanArtifact(MavenNames.ArtifactName(r.GroupId!, r.ArtifactId!), r.Version!)).ToList();
    }
}
