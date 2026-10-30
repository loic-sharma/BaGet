using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NuGet.Packaging;
using NuGet.Versioning;

namespace BaGet.Core;

/// <summary>
/// What a NuGet package is made of for scanning: itself and its dependencies at the lowest version their range allows (the version
/// NuGet restores). Dependencies without a lower bound are skipped.
/// </summary>
public static class NuGetScanComponents
{
    /// <summary>The OSV ecosystem name.</summary>
    public const string Ecosystem = "NuGet";

    /// <summary>The package and its dependencies.</summary>
    /// <param name="id">The package id.</param>
    /// <param name="version">The package version.</param>
    /// <param name="dependencies">The declared dependencies as (id, version range) pairs, all target frameworks together.</param>
    public static IReadOnlyList<ScanComponent> Of(string id, string version, IEnumerable<(string? Id, string? Range)> dependencies)
    {
        var result = new List<ScanComponent> { new(Ecosystem, id, NormalizeVersion(version)) };
        foreach (var (dependencyId, range) in dependencies)
        {
            if (string.IsNullOrWhiteSpace(dependencyId) || string.IsNullOrWhiteSpace(range)) continue;
            if (!VersionRange.TryParse(range, out var parsed) || parsed.MinVersion == null) continue;

            var component = new ScanComponent(Ecosystem, dependencyId, parsed.MinVersion.ToNormalizedString());
            if (!result.Contains(component)) result.Add(component);
        }

        return result;
    }

    /// <summary>
    /// The components of a <c>.nupkg</c> being pushed, or null when the stream is not a readable package (the indexer then refuses
    /// it with its own message). The stream is rewound.
    /// </summary>
    public static IReadOnlyList<ScanComponent>? TryRead(Stream package)
    {
        if (package == null) throw new ArgumentNullException(nameof(package));

        try
        {
            package.Position = 0;
            using var reader = new PackageArchiveReader(package, leaveStreamOpen: true);
            var identity = reader.GetIdentity();
            var dependencies = reader.GetPackageDependencies()
                .SelectMany(g => g.Packages)
                .Select(d => ((string?)d.Id, (string?)d.VersionRange.ToNormalizedString()));
            return Of(identity.Id, identity.Version.ToNormalizedString(), dependencies);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
        finally
        {
            try { package.Position = 0; } catch (NotSupportedException) { }
        }
    }

    private static string NormalizeVersion(string version)
        => NuGetVersion.TryParse(version, out var parsed) ? parsed.ToNormalizedString() : version;
}

/// <summary>NuGet's <see cref="IFormatScanner"/>: the repository's packages and their declared dependencies against OSV.</summary>
public sealed class NuGetFormatScanner : IFormatScanner
{
    private readonly IContext _context;

    public NuGetFormatScanner(IContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc/>
    public RepositoryFormat Format => RepositoryFormat.NuGet;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ScanComponent>> ListComponentsAsync(string name, string version, CancellationToken cancellationToken)
    {
        if (_context is NullContext) return Array.Empty<ScanComponent>();
        var normalized = NuGetVersion.TryParse(version, out var parsed) ? parsed.ToNormalizedString() : version;

        var package = await _context.Packages!
            .AsNoTracking()
            .Include(p => p.Dependencies)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == name && p.NormalizedVersionString == normalized, cancellationToken)
            .ConfigureAwait(false);
        if (package == null) return Array.Empty<ScanComponent>();

        return NuGetScanComponents.Of(
            package.Id!,
            package.NormalizedVersionString!,
            (package.Dependencies ?? new List<PackageDependency>()).Select(d => (d.Id, d.VersionRange)));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ScanArtifact>> ListArtifactsAsync(CancellationToken cancellationToken)
    {
        if (_context is NullContext) return Array.Empty<ScanArtifact>();

        var rows = await _context.Packages!
            .AsNoTracking()
            .Select(p => new { p.Id, p.NormalizedVersionString })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(r => new ScanArtifact(r.Id!, r.NormalizedVersionString!)).ToList();
    }
}
