using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core;

/// <summary>
/// Docker / OCI on the format layer: the Distribution Spec v1.1 at <c>/v2/{repository}/{image}/…</c> (the BaGet repository is the first
/// path segment, so <c>docker push host/libs/team/app:1</c> pushes image <c>team/app</c> into repository <c>libs</c>), blobs by
/// digest under <c>docker/</c> in storage. A local repository holds pushed images, a remote one caches an upstream registry
/// (<see cref="RemoteOciRegistry"/>), a virtual one merges members (<see cref="VirtualOciRegistry"/>). Needs a store that streams
/// (<see cref="RequiresStreamingStorage"/>). See <see cref="IOciRegistry"/>.
/// </summary>
public class DockerFormatModule : IFormatModule
{
    /// <inheritdoc/>
    public RepositoryFormat Format => RepositoryFormat.Docker;

    /// <inheritdoc/>
    public string DisplayName => "Docker";

    /// <inheritdoc/>
    public string RoutePrefix => "v2";

    /// <inheritdoc/>
    /// <remarks>
    /// A registry is addressed from the server root, not from the repository base, and the repository is part of the image
    /// reference: <c>{repo}</c> stands for the repository's name, and the path is relative to the server's root (the NuGet
    /// <c>/r/{repo}/</c> prefix does not apply).
    /// </remarks>
    public string FeedPath => "v2/{repo}/";

    /// <inheritdoc/>
    public IReadOnlyCollection<RepositoryKind> CreatableKinds { get; } = new[] { RepositoryKind.Local, RepositoryKind.Remote };

    /// <inheritdoc/>
    public bool SupportsVirtual => true;

    /// <inheritdoc/>
    public bool RequiresStreamingStorage => true;

    /// <inheritdoc/>
    public IReadOnlyList<string> StorageFolders { get; } = new[] { OciNames.StorageFolder };

    /// <inheritdoc/>
    public IReadOnlyList<FormatCreateField> CreateFields { get; } = Array.Empty<FormatCreateField>();

    /// <inheritdoc/>
    public string? CreateSettings(IReadOnlyDictionary<string, string?> fields) => null;

    /// <inheritdoc/>
    public IReadOnlyList<FormatLink> RepositoryLinks(RepositoryInfo repository) => new[]
    {
        FormatLink.ToScreen("browse", "Browse", "DockerBrowse", "Images"),
    };

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<int, int>> CountArtifactsAsync(IContext context, CancellationToken cancellationToken)
        => await context.OciRepositories!
            .IgnoreQueryFilters()
            .GroupBy(i => i.RepositoryKey)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<FormatUsage> GetUsageAsync(IContext context, int repositoryKey, CancellationToken cancellationToken)
    {
        var images = await context.OciRepositories!.IgnoreQueryFilters().CountAsync(i => i.RepositoryKey == repositoryKey, cancellationToken).ConfigureAwait(false);

        return new FormatUsage(images, PendingWork: false);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="GetUsageAsync"/> counts image rows, and a repository is deleted only when it has none, so the images and with them
    /// the manifests, tags, links and upload sessions are already gone here; what can be left are blob rows nobody holds.
    /// </remarks>
    public async Task StagePurgeAsync(IContext context, int repositoryKey, CancellationToken cancellationToken)
    {
        context.OciBlobs!.RemoveRange(await context.OciBlobs!.IgnoreQueryFilters().Where(b => b.RepositoryKey == repositoryKey).ToListAsync(cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Docker's <see cref="IFormatSearchProvider"/>: images whose name contains the query.</summary>
public class DockerFormatSearchProvider : IFormatSearchProvider
{
    private readonly IOciRegistry _registry;

    /// <param name="registry">The registry of the request.</param>
    public DockerFormatSearchProvider(IOciRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <inheritdoc/>
    public RepositoryFormat Format => RepositoryFormat.Docker;

    /// <inheritdoc/>
    public async Task<FormatSearchPage> SearchAsync(FormatSearchQuery query, CancellationToken cancellationToken)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));

        var page = await _registry.ListImagesAsync(query.Query, query.Skip, query.Take, canRead: null, cancellationToken).ConfigureAwait(false);

        return new FormatSearchPage(
            page.Total,
            page.Images.Select(i => new FormatSearchHit(i.Name, null, $"{i.Tags} tag{(i.Tags == 1 ? string.Empty : "s")}", Downloads: 0)).ToList());
    }
}
