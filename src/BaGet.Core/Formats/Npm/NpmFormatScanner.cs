using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NuGet.Versioning;

namespace BaGet.Core;

/// <summary>
/// What an npm package version is made of for scanning: itself and its <c>dependencies</c> that name one exact version (a range
/// such as <c>^1.2.0</c> is resolved by the client at install time and cannot be judged here).
/// </summary>
public static class NpmScanComponents
{
    /// <summary>The OSV ecosystem name.</summary>
    public const string Ecosystem = "npm";

    /// <summary>The components of <paramref name="manifest"/> (the version's <c>package.json</c>).</summary>
    public static IReadOnlyList<ScanComponent> Of(string name, string version, JsonObject? manifest)
    {
        var result = new List<ScanComponent> { new(Ecosystem, name, version) };
        if (manifest?["dependencies"] is JsonObject dependencies)
        {
            foreach (var dependency in dependencies)
            {
                if (dependency.Value is not JsonValue value || !value.TryGetValue<string>(out var range)) continue;

                var exact = range.Trim();
                if (exact.Length == 0 || !char.IsDigit(exact[0]) || !NuGetVersion.TryParse(exact, out _)) continue;

                var component = new ScanComponent(Ecosystem, dependency.Key, exact);
                if (!result.Contains(component)) result.Add(component);
            }
        }

        return result;
    }
}

/// <summary>npm's <see cref="IFormatScanner"/>: the stored versions and their pinned dependencies against OSV.</summary>
public sealed class NpmFormatScanner : IFormatScanner
{
    private readonly IContext _context;

    public NpmFormatScanner(IContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc/>
    public RepositoryFormat Format => RepositoryFormat.Npm;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ScanComponent>> ListComponentsAsync(string name, string version, CancellationToken cancellationToken)
    {
        if (_context is NullContext) return Array.Empty<ScanComponent>();

        var manifest = await _context.NpmVersions!
            .AsNoTracking()
            .Join(_context.NpmPackages!.Where(p => p.Name == name), v => v.PackageKey, p => p.Key, (v, p) => new { v.Version, v.Manifest })
            .Where(v => v.Version == version)
            .Select(v => v.Manifest)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (manifest == null) return Array.Empty<ScanComponent>();

        JsonObject? json = null;
        try
        {
            json = JsonNode.Parse(manifest) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            // A manifest that does not parse still names the package itself.
        }

        return NpmScanComponents.Of(name, version, json);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ScanArtifact>> ListArtifactsAsync(CancellationToken cancellationToken)
    {
        if (_context is NullContext) return Array.Empty<ScanArtifact>();

        var rows = await _context.NpmVersions!
            .AsNoTracking()
            .Join(_context.NpmPackages!, v => v.PackageKey, p => p.Key, (v, p) => new { p.Name, v.Version })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(r => new ScanArtifact(r.Name!, r.Version!)).ToList();
    }
}
