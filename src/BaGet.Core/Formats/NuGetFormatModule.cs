using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core;

/// <summary>
/// NuGet on the format layer: V3 (<c>v3/…</c>), V2 (<c>api/v2</c>) and symbol routes at the repository base, packages and symbols
/// in the folders that predate repositories. Its behaviour is exactly what the code did before the layer existed; this class only
/// names it, so the common code no longer assumes NuGet.
/// </summary>
public class NuGetFormatModule : IFormatModule
{
    /// <inheritdoc/>
    public RepositoryFormat Format => RepositoryFormat.NuGet;

    /// <inheritdoc/>
    public string DisplayName => "NuGet";

    /// <inheritdoc/>
    public bool SupportsVirtual => true;

    /// <inheritdoc/>
    public string RoutePrefix => string.Empty;

    /// <inheritdoc/>
    public string FeedPath => "v3/index.json";

    /// <inheritdoc/>
    public IReadOnlyCollection<RepositoryKind> CreatableKinds { get; } = new[] { RepositoryKind.Local, RepositoryKind.Remote };

    /// <inheritdoc/>
    public IReadOnlyList<string> StorageFolders { get; } = new[] { "packages", "symbols", "catalog", "validation", "staging" };

    /// <inheritdoc/>
    /// <remarks>None: a NuGet repository is a name (a remote one has its own form with the upstream settings).</remarks>
    public IReadOnlyList<FormatCreateField> CreateFields { get; } = Array.Empty<FormatCreateField>();

    /// <inheritdoc/>
    public string? CreateSettings(IReadOnlyDictionary<string, string?> fields) => null;

    /// <inheritdoc/>
    /// <remarks>
    /// Browse and upload, then the protocol resources a client or an operator can open besides the service index: the V2 feed,
    /// the vulnerability index, and (a local repository only, the others have no log or signer of their own) the catalog and the
    /// repository signatures.
    /// </remarks>
    public IReadOnlyList<FormatLink> RepositoryLinks(RepositoryInfo repository)
    {
        var links = new List<FormatLink>
        {
            FormatLink.ToScreen("browse", "Browse", "Home", "Index"),
            FormatLink.ToScreen("upload", "Upload", "Upload", "Index", needsUploads: true),
            FormatLink.ToPath("v2", "V2 feed", "api/v2"),
            FormatLink.ToPath("vulnerabilities", "Vulnerabilities", "v3/vulnerabilities/index.json"),
        };

        if (repository.Kind == RepositoryKind.Local)
        {
            links.Add(FormatLink.ToPath("catalog", "Catalog", "v3/catalog/index.json"));
            links.Add(FormatLink.ToPath("signatures", "Signatures", "v3/repository-signatures/index.json"));
        }

        return links;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<int, int>> CountArtifactsAsync(IContext context, CancellationToken cancellationToken)
        => await context.Packages!
            .IgnoreQueryFilters()
            .GroupBy(p => p.RepositoryKey)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<FormatUsage> GetUsageAsync(IContext context, int repositoryKey, CancellationToken cancellationToken)
    {
        var packages = await context.Packages!.IgnoreQueryFilters().CountAsync(e => e.RepositoryKey == repositoryKey, cancellationToken).ConfigureAwait(false);

        // A pushed package the client already got an accepted response for, staged but not indexed yet: deleting loses it.
        var pending = await context.PackageValidations!.IgnoreQueryFilters()
            .AnyAsync(e => e.RepositoryKey == repositoryKey && e.State == PackageValidationState.Pending, cancellationToken).ConfigureAwait(false);

        return new FormatUsage(packages, pending);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The catalog is an append-only log (deleting a package adds an entry) and owner rows outlive their package, so those, the
    /// finished validations, the abuse reports and the symbol records are history of a repository that no longer exists.
    /// </remarks>
    public async Task StagePurgeAsync(IContext context, int repositoryKey, CancellationToken cancellationToken)
    {
        context.CatalogLeafs!.RemoveRange(await context.CatalogLeafs!.IgnoreQueryFilters().Where(e => e.RepositoryKey == repositoryKey).ToListAsync(cancellationToken).ConfigureAwait(false));
        context.PackageOwners!.RemoveRange(await context.PackageOwners!.IgnoreQueryFilters().Where(e => e.RepositoryKey == repositoryKey).ToListAsync(cancellationToken).ConfigureAwait(false));
        context.PackageValidations!.RemoveRange(await context.PackageValidations!.IgnoreQueryFilters().Where(e => e.RepositoryKey == repositoryKey).ToListAsync(cancellationToken).ConfigureAwait(false));
        context.AbuseReports!.RemoveRange(await context.AbuseReports!.IgnoreQueryFilters().Where(e => e.RepositoryKey == repositoryKey).ToListAsync(cancellationToken).ConfigureAwait(false));
        context.PackageSymbols!.RemoveRange(await context.PackageSymbols!.IgnoreQueryFilters().Where(e => e.RepositoryKey == repositoryKey).ToListAsync(cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>NuGet's <see cref="IFormatIndexer"/>: the push pipeline (<see cref="IPackageIndexingService"/>).</summary>
public class NuGetFormatIndexer : IFormatIndexer
{
    private readonly IPackageIndexingService _indexing;

    /// <param name="indexing">The service that validates, stores and records a package.</param>
    public NuGetFormatIndexer(IPackageIndexingService indexing)
    {
        _indexing = indexing ?? throw new ArgumentNullException(nameof(indexing));
    }

    /// <inheritdoc/>
    public RepositoryFormat Format => RepositoryFormat.NuGet;

    /// <inheritdoc/>
    public async Task<FormatIndexResult> IndexAsync(Stream? artifact, CancellationToken cancellationToken)
        => await _indexing.IndexAsync(artifact, cancellationToken).ConfigureAwait(false) switch
        {
            PackageIndexingResult.Success => FormatIndexResult.Indexed,
            PackageIndexingResult.Accepted => FormatIndexResult.Accepted,
            PackageIndexingResult.PackageAlreadyExists => FormatIndexResult.AlreadyExists,
            _ => FormatIndexResult.Invalid,
        };
}

/// <summary>NuGet's <see cref="IFormatSearchProvider"/>: the registered <see cref="ISearchService"/> (so a virtual repository merges its members).</summary>
public class NuGetFormatSearchProvider : IFormatSearchProvider
{
    private readonly ISearchService _search;

    /// <param name="search">The search service of the host.</param>
    public NuGetFormatSearchProvider(ISearchService search)
    {
        _search = search ?? throw new ArgumentNullException(nameof(search));
    }

    /// <inheritdoc/>
    public RepositoryFormat Format => RepositoryFormat.NuGet;

    /// <inheritdoc/>
    public async Task<FormatSearchPage> SearchAsync(FormatSearchQuery query, CancellationToken cancellationToken)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));

        var response = await _search.SearchAsync(
            new SearchRequest
            {
                Query = query.Query,
                Skip = query.Skip,
                Take = query.Take,
                IncludePrerelease = query.IncludePrerelease,
                IncludeSemVer2 = true,
            },
            cancellationToken).ConfigureAwait(false);

        var hits = (response.Data ?? Array.Empty<BaGet.Protocol.Models.SearchResult>())
            .Select(r => new FormatSearchHit(r.PackageId ?? string.Empty, r.Version, r.Description, r.TotalDownloads))
            .ToList();

        return new FormatSearchPage(response.TotalHits, hits);
    }
}
