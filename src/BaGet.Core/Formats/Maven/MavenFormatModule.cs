using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core;

/// <summary>
/// Maven on the format layer: the Maven 2 repository layout under <c>maven/</c> of the repository base (never served for
/// <c>default</c>, which is NuGet; a repository <c>libs</c> is at <c>/r/libs/maven/</c>), files under <c>maven/…</c> in storage.
/// A local repository holds artifacts (its release/snapshot policy, redeploy and snapshot retention are its
/// <see cref="MavenRepositorySettings"/>); a remote one caches an upstream such as Maven Central (<see cref="RemoteMavenRepository"/>);
/// a virtual one merges members (<see cref="VirtualMavenRepository"/>). See <see cref="IMavenRepository"/>.
/// </summary>
public class MavenFormatModule : IFormatModule
{
    /// <inheritdoc/>
    public RepositoryFormat Format => RepositoryFormat.Maven;

    /// <inheritdoc/>
    public string DisplayName => "Maven";

    /// <inheritdoc/>
    public string RoutePrefix => "maven";

    /// <inheritdoc/>
    public string FeedPath => "maven/";

    /// <inheritdoc/>
    public IReadOnlyCollection<RepositoryKind> CreatableKinds { get; } = new[] { RepositoryKind.Local, RepositoryKind.Remote };

    /// <inheritdoc/>
    public bool SupportsVirtual => true;

    /// <inheritdoc/>
    public IReadOnlyList<string> StorageFolders { get; } = new[] { MavenNames.StorageFolder };

    /// <inheritdoc/>
    public IReadOnlyList<FormatCreateField> CreateFields { get; } = new[]
    {
        new FormatCreateField("policy", "Versions accepted", "Releases (the default), -SNAPSHOT versions, or both.", Options: new[] { "Releases", "Snapshots", "Both" }),
        new FormatCreateField("redeploy", "Redeploy releases", "Whether a release file that exists may be replaced (needs the Delete permission). Default: no.", Options: new[] { "No", "Yes" }),
        new FormatCreateField("snapshotBuilds", "Snapshot builds kept", "How many timestamped builds of a snapshot are kept (0: all). Older ones are deleted when a newer build arrives."),
    };

    /// <inheritdoc/>
    public string? CreateSettings(IReadOnlyDictionary<string, string?> fields) => MavenRepositorySettings.FromFields(fields).ToJson();

    /// <inheritdoc/>
    public IReadOnlyList<FormatLink> RepositoryLinks(RepositoryInfo repository) => new[]
    {
        FormatLink.ToScreen("browse", "Browse", "MavenBrowse", "Artifacts"),
        FormatLink.ToScreen("upload", "Upload", "MavenBrowse", "Upload", needsUploads: true),
        FormatLink.ToPath("repository", "Repository", "maven/"),
    };

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<int, int>> CountArtifactsAsync(IContext context, CancellationToken cancellationToken)
        => await context.MavenArtifacts!
            .IgnoreQueryFilters()
            .GroupBy(a => a.RepositoryKey)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<FormatUsage> GetUsageAsync(IContext context, int repositoryKey, CancellationToken cancellationToken)
    {
        var artifacts = await context.MavenArtifacts!.IgnoreQueryFilters().CountAsync(e => e.RepositoryKey == repositoryKey, cancellationToken).ConfigureAwait(false);

        return new FormatUsage(artifacts, PendingWork: false);
    }

    /// <inheritdoc/>
    /// <remarks>An empty repository has no artifacts, so versions and files (which cascade) are gone; what is left is the owner rows.</remarks>
    public async Task StagePurgeAsync(IContext context, int repositoryKey, CancellationToken cancellationToken)
    {
        context.MavenArtifacts!.RemoveRange(await context.MavenArtifacts!.IgnoreQueryFilters().Where(e => e.RepositoryKey == repositoryKey).ToListAsync(cancellationToken).ConfigureAwait(false));
        context.PackageOwners!.RemoveRange(await context.PackageOwners!.IgnoreQueryFilters().Where(e => e.RepositoryKey == repositoryKey).ToListAsync(cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Maven's <see cref="IFormatSearchProvider"/>: artifacts whose coordinates or POM name or description contain the query.</summary>
public class MavenFormatSearchProvider : IFormatSearchProvider
{
    private readonly IMavenRepository _repository;

    /// <param name="repository">The repository of the request.</param>
    public MavenFormatSearchProvider(IMavenRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <inheritdoc/>
    public RepositoryFormat Format => RepositoryFormat.Maven;

    /// <inheritdoc/>
    public async Task<FormatSearchPage> SearchAsync(FormatSearchQuery query, CancellationToken cancellationToken)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));

        var page = await _repository.SearchAsync(query.Query, query.Skip, query.Take, canRead: null, cancellationToken).ConfigureAwait(false);

        return new FormatSearchPage(
            page.Total,
            page.Hits
                .Where(h => query.IncludePrerelease || h.Version == null || !MavenNames.IsSnapshot(h.Version))
                .Select(h => new FormatSearchHit(h.CoordinateName, h.Version, h.Description ?? h.Name, Downloads: 0))
                .ToList());
    }
}
