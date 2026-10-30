using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core;

/// <summary>
/// Docker's <see cref="IFormatScanner"/>: container images are scanned by Trivy (<see cref="ScanEngine.Image"/>), one result per
/// image manifest, named by image and manifest digest. Indexes (multi-platform lists), signatures and other artifacts that refer
/// to an image are not images and are not scanned; each platform's manifest is.
/// </summary>
public sealed class DockerFormatScanner : IFormatScanner
{
    private readonly IContext _context;

    public DockerFormatScanner(IContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc/>
    public RepositoryFormat Format => RepositoryFormat.Docker;

    /// <inheritdoc/>
    public ScanEngine Engine => ScanEngine.Image;

    /// <inheritdoc/>
    public Task<IReadOnlyList<ScanComponent>> ListComponentsAsync(string name, string version, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ScanComponent>>(Array.Empty<ScanComponent>());

    /// <inheritdoc/>
    public async Task<string?> GetImageReferenceAsync(string name, string version, CancellationToken cancellationToken)
    {
        if (_context is NullContext || !OciNames.IsValidDigest(version)) return null;

        // Only an image manifest is scannable (not an index, a signature or another artifact), so only it is "stored" for the gate.
        var row = await _context.OciManifests!
            .AsNoTracking()
            .Join(_context.OciRepositories!.Where(r => r.Name == name), m => m.ImageKey, r => r.Key, (m, r) => new { m.Digest, m.MediaType, m.Content })
            .Where(m => m.Digest == version)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return row != null && IsImage(row.MediaType, row.Content) ? name + "@" + version : null;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ScanArtifact>> ListArtifactsAsync(CancellationToken cancellationToken)
    {
        if (_context is NullContext) return Array.Empty<ScanArtifact>();

        // Indexes and other artifacts are cut in SQL, so their (large) content is never loaded.
        var imageTypes = OciNames.ImageManifestTypes.ToArray();
        var rows = await _context.OciManifests!
            .AsNoTracking()
            .Where(m => imageTypes.Contains(m.MediaType!))
            .Join(_context.OciRepositories!, m => m.ImageKey, r => r.Key, (m, r) => new { r.Name, m.Digest, m.MediaType, m.Content })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows
            .Where(r => IsImage(r.MediaType, r.Content))
            .Select(r => new ScanArtifact(r.Name!, r.Digest!))
            .ToList();
    }

    /// <summary>
    /// Whether a manifest is a container image (not an index, a signature or another artifact). Decided from the manifest's media type
    /// and its config descriptor's media type only: <c>artifactType</c> and <c>subject</c> are self-declared and ignored by the
    /// runtimes that run the image, so they must not take an image out of scanning.
    /// </summary>
    public static bool IsImage(string? mediaType, string? content)
    {
        if (mediaType == null || !OciNames.ImageManifestTypes.Contains(mediaType, StringComparer.Ordinal) || content == null) return false;

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("config", out var config)
                || config.ValueKind != JsonValueKind.Object
                || !config.TryGetProperty("mediaType", out var configType)
                || configType.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var type = configType.GetString();
            return string.Equals(type, "application/vnd.oci.image.config.v1+json", StringComparison.Ordinal)
                   || string.Equals(type, "application/vnd.docker.container.image.v1+json", StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
