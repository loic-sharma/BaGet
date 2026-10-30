using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core;

/// <summary>
/// Docker's <see cref="IFormatRetentionHooks"/>: a version is a <b>tag</b> of an image (name = the image, version = the tag), deleting
/// it removes the tag only; the manifest it pointed at stays until it is untagged long enough for the untagged rule
/// (<see cref="ListUnreferencedAsync"/>: manifests no tag, no index and no referrer relationship needs, version = digest), and the
/// blobs go with the garbage collection (<see cref="IOciGarbageCollector"/>, which a policy can run after its rules). A tag counts as
/// downloaded when it or its digest was pulled.
/// </summary>
public sealed class DockerRetentionHooks : IFormatRetentionHooks
{
    private readonly IContext _context;
    private readonly OciRegistryService _registry;

    public DockerRetentionHooks(IContext context, OciRegistryService registry)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <inheritdoc/>
    public RepositoryFormat Format => RepositoryFormat.Docker;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FormatVersion>> ListVersionsAsync(CancellationToken cancellationToken)
    {
        var usage = await ArtifactUsageMap.LoadAsync(_context, cancellationToken).ConfigureAwait(false);
        var rows = await (from t in _context.OciTags!.AsNoTracking()
                          join m in _context.OciManifests!.AsNoTracking() on t.ManifestKey equals m.Key
                          join i in _context.OciRepositories!.AsNoTracking() on m.ImageKey equals i.Key
                          select new { Image = i.Name, Tag = t.Name, t.Updated, m.Digest }).ToListAsync(cancellationToken).ConfigureAwait(false);

        // A tag has no prerelease notion; its size (the layers it uses) is worked out by GetSizeAsync when a cache limit needs it.
        return rows
            .Select(r => new FormatVersion(r.Image!, r.Tag!, DateTime.SpecifyKind(r.Updated, DateTimeKind.Utc), usage.Latest(r.Image!, r.Tag!, r.Digest!), null, false))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FormatVersion>> ListUnreferencedAsync(CancellationToken cancellationToken)
    {
        var usage = await ArtifactUsageMap.LoadAsync(_context, cancellationToken).ConfigureAwait(false);
        var manifests = await (from m in _context.OciManifests!.AsNoTracking()
                               join i in _context.OciRepositories!.AsNoTracking() on m.ImageKey equals i.Key
                               select new { m.Key, m.ImageKey, Image = i.Name, m.Digest, m.MediaType, m.SubjectDigest, m.Created, m.Size }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var tagged = (await (from t in _context.OciTags!.AsNoTracking()
                             join m in _context.OciManifests!.AsNoTracking() on t.ManifestKey equals m.Key
                             join i in _context.OciRepositories!.AsNoTracking() on m.ImageKey equals i.Key
                             select t.ManifestKey).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();

        // What an index names as a child is in use as long as the index is; an index is read to find them.
        var indexKeys = manifests.Where(m => OciNames.IsIndex(m.MediaType)).Select(m => m.Key).ToList();
        var indexes = indexKeys.Count == 0
            ? new List<(int ImageKey, string Content, string? MediaType)>()
            : (await _context.OciManifests!.AsNoTracking().Where(m => indexKeys.Contains(m.Key)).Select(m => new { m.ImageKey, m.Content, m.MediaType }).ToListAsync(cancellationToken).ConfigureAwait(false))
                .Select(m => (ImageKey: m.ImageKey, Content: m.Content!, MediaType: m.MediaType)).ToList();
        var children = new HashSet<(int, string)>();
        foreach (var index in indexes)
        {
            try
            {
                foreach (var child in OciManifestParser.Parse(index.MediaType, Encoding.UTF8.GetBytes(index.Content)).Children) children.Add((index.ImageKey, child.Descriptor.Digest));
            }
            catch (OciException)
            {
                // A document that no longer parses names nothing.
            }
        }

        var digests = manifests.Select(m => (m.ImageKey, m.Digest!)).ToHashSet();

        return manifests
            .Where(m => !tagged.Contains(m.Key)
                        && !children.Contains((m.ImageKey, m.Digest!))
                        && !(m.SubjectDigest != null && digests.Contains((m.ImageKey, m.SubjectDigest))))
            .Select(m => new FormatVersion(m.Image!, m.Digest!, DateTime.SpecifyKind(m.Created, DateTimeKind.Utc), usage.Get(m.Image!, m.Digest!), m.Size, false))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task DeleteVersionAsync(string name, string version, CancellationToken cancellationToken)
    {
        if (OciNames.LooksLikeDigest(version)) await _registry.DeleteManifestAsync(name, version, cancellationToken).ConfigureAwait(false);
        else await _registry.DeleteTagAsync(name, version, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<long?> GetSizeAsync(string name, string version, CancellationToken cancellationToken)
    {
        var manifest = await (from t in _context.OciTags!.AsNoTracking()
                              join m in _context.OciManifests!.AsNoTracking() on t.ManifestKey equals m.Key
                              join i in _context.OciRepositories!.AsNoTracking() on m.ImageKey equals i.Key
                              where i.Name == name && t.Name == version
                              select new { m.ImageKey, m.Digest, m.MediaType, m.Content, m.Size }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (manifest == null) return null;

        // The manifest, plus (for an index) the manifests it lists, plus each distinct blob they use once.
        long total = manifest.Size;
        var blobs = new HashSet<string>(StringComparer.Ordinal);
        var documents = new List<(string? MediaType, string Content)> { (manifest.MediaType, manifest.Content!) };
        for (var i = 0; i < documents.Count && i < 200; i++)
        {
            OciParsedManifest parsed;
            try
            {
                parsed = OciManifestParser.Parse(documents[i].MediaType, Encoding.UTF8.GetBytes(documents[i].Content));
            }
            catch (OciException)
            {
                continue;
            }

            if (parsed.IsIndex)
            {
                var digests = parsed.Children.Select(c => c.Descriptor.Digest).ToList();
                var rows = await _context.OciManifests!.AsNoTracking().Where(m => m.ImageKey == manifest.ImageKey && digests.Contains(m.Digest!))
                    .Select(m => new { m.MediaType, m.Content, m.Size }).ToListAsync(cancellationToken).ConfigureAwait(false);
                foreach (var row in rows)
                {
                    total += row.Size;
                    documents.Add((row.MediaType, row.Content!));
                }
            }
            else
            {
                foreach (var digest in parsed.BlobDigests) blobs.Add(digest);
            }
        }

        if (blobs.Count > 0)
        {
            var list = blobs.ToList();
            total += await _context.OciBlobs!.AsNoTracking().Where(b => list.Contains(b.Digest!)).SumAsync(b => b.Size, cancellationToken).ConfigureAwait(false);
        }

        return total;
    }

    /// <inheritdoc/>
    public (string Name, string Version)? Canonical(string name, string? version)
    {
        var image = name?.Trim().ToLowerInvariant();
        if (!OciNames.IsValidImage(image)) return null;
        if (string.IsNullOrWhiteSpace(version)) return (image!, string.Empty);

        var v = version.Trim();

        return OciNames.IsValidDigest(v) || OciNames.IsValidTag(v) ? (image!, v) : null;
    }
}
