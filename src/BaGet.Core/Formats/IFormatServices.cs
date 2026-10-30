using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Core;

/// <summary>
/// A per-request capability of one format, registered (scoped) beside its <see cref="IFormatModule"/>. It works in the
/// request's repository (<see cref="IRepositoryContext"/>), like every other scoped service.
/// </summary>
public interface IFormatService
{
    /// <summary>The format the service belongs to.</summary>
    RepositoryFormat Format { get; }
}

/// <summary>Finds the capability of a format.</summary>
public static class FormatServices
{
    /// <summary>
    /// The registered <typeparamref name="T"/> of <paramref name="format"/>, or null when the format does not offer it (a
    /// phase that has not implemented it yet). Callers treat null as "not supported", never as an error.
    /// </summary>
    public static T? GetFormatService<T>(this IServiceProvider provider, RepositoryFormat format)
        where T : class, IFormatService
        => provider.GetServices<T>().FirstOrDefault(s => s.Format == format);
}

/// <summary>What happened to an artifact handed to <see cref="IFormatIndexer.IndexAsync"/>.</summary>
public enum FormatIndexResult
{
    /// <summary>The bytes are not a valid artifact of the format.</summary>
    Invalid,

    /// <summary>The same name and version is already in the repository.</summary>
    AlreadyExists,

    /// <summary>Stored and visible.</summary>
    Indexed,

    /// <summary>Stored and queued (validation); not visible yet.</summary>
    Accepted,
}

/// <summary>Reads an artifact, stores it and records its metadata in the repository of the request (deploy).</summary>
public interface IFormatIndexer : IFormatService
{
    /// <summary>Indexes the artifact in <paramref name="artifact"/>.</summary>
    Task<FormatIndexResult> IndexAsync(Stream? artifact, CancellationToken cancellationToken);
}

/// <summary>A search in one repository.</summary>
/// <param name="Query">The text, or null for everything.</param>
/// <param name="Skip">Results to skip.</param>
/// <param name="Take">Results to return.</param>
/// <param name="IncludePrerelease">Whether prerelease versions count.</param>
public sealed record FormatSearchQuery(string? Query, int Skip, int Take, bool IncludePrerelease);

/// <summary>One search result: an artifact name with its newest version.</summary>
public sealed record FormatSearchHit(string Name, string? Version, string? Description, long Downloads);

/// <summary>A page of search results.</summary>
/// <param name="TotalHits">All matches, not only this page.</param>
/// <param name="Hits">This page.</param>
public sealed record FormatSearchPage(long TotalHits, IReadOnlyList<FormatSearchHit> Hits);

/// <summary>Searches the repository of the request in the format's own terms (the protocol's search endpoint uses its richer model).</summary>
public interface IFormatSearchProvider : IFormatService
{
    /// <summary>Runs <paramref name="query"/>.</summary>
    Task<FormatSearchPage> SearchAsync(FormatSearchQuery query, CancellationToken cancellationToken);
}

/// <summary>A stored version, as retention policies see it.</summary>
/// <param name="Name">The artifact name (package id, npm name, Maven coordinates).</param>
/// <param name="Version">The version.</param>
/// <param name="Published">When it was stored.</param>
/// <param name="LastDownloaded">The last download, when known.</param>
/// <param name="Size">Bytes stored for it, when known.</param>
/// <param name="Prerelease">Whether it is a prerelease (npm), a pre-release (NuGet) or a snapshot (Maven).</param>
public sealed record FormatVersion(string Name, string Version, DateTime Published, DateTime? LastDownloaded, long? Size, bool Prerelease);

/// <summary>
/// What retention (phase P6) needs from a format: list what is stored and delete one version, in the repository of the request
/// (the retention engine runs each repository in its own scope). Implemented for NuGet, npm, Maven and Docker. Deleting is the
/// administrator's cleanup, not a user action: no caller is checked, but the maintenance gate is (<see cref="MaintenanceException"/> is
/// never swallowed by the engine).
/// </summary>
public interface IFormatRetentionHooks : IFormatService
{
    /// <summary>
    /// Every stored version of the repository that a policy may delete (a Docker tag is a version of its image). A virtual
    /// repository stores nothing and has none.
    /// </summary>
    Task<IReadOnlyList<FormatVersion>> ListVersionsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Deletes one version for good (metadata and files). The name and version are the ones <see cref="ListVersionsAsync"/> or
    /// <see cref="ListUnreferencedAsync"/> returned.
    /// </summary>
    Task DeleteVersionAsync(string name, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Things the format stores that no version points at, which only the "untagged" rule considers (Docker manifests without a
    /// tag, index or referrer relationship; <see cref="FormatVersion.Version"/> is their digest). Default: none.
    /// </summary>
    Task<IReadOnlyList<FormatVersion>> ListUnreferencedAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<FormatVersion>>(Array.Empty<FormatVersion>());

    /// <summary>
    /// Bytes stored for one version, when <see cref="FormatVersion.Size"/> was left null by the listing because measuring is costly
    /// (NuGet reads the package, Docker adds its layers); null when unknown. Only the remote cache limit asks.
    /// </summary>
    Task<long?> GetSizeAsync(string name, string version, CancellationToken cancellationToken)
        => Task.FromResult<long?>(null);

    /// <summary>
    /// The name and version in the form the listing uses (NuGet: normalized version; a legal hold is stored this way), or null
    /// when they are not valid for the format. An empty or null <paramref name="version"/> stays empty (every version). Default: trimmed as given.
    /// </summary>
    (string Name, string Version)? Canonical(string name, string? version)
        => string.IsNullOrWhiteSpace(name) ? null : (name.Trim(), version?.Trim() ?? string.Empty);
}
/// <summary>A third-party component of an artifact, named the way a vulnerability database names it.</summary>
/// <param name="Ecosystem">The OSV ecosystem: <c>NuGet</c>, <c>npm</c>, <c>Maven</c>.</param>
/// <param name="Name">The component name in that ecosystem.</param>
/// <param name="Version">Its version.</param>
public sealed record ScanComponent(string Ecosystem, string Name, string Version);

/// <summary>An artifact version to scan, named as <see cref="ScanResult"/> names it.</summary>
/// <param name="Name">The artifact name (NuGet id, npm name, Maven <c>group:artifact</c>, Docker image).</param>
/// <param name="Version">The version (Docker: the manifest digest).</param>
public sealed record ScanArtifact(string Name, string Version);

/// <summary>How a format's artifacts are checked.</summary>
public enum ScanEngine
{
    /// <summary>The artifact's components are looked up in OSV (<see cref="IFormatScanner.ListComponentsAsync"/>).</summary>
    Osv,

    /// <summary>The artifact is a container image that Trivy pulls and scans (<see cref="IFormatScanner.GetImageReferenceAsync"/>).</summary>
    Image,
}

/// <summary>
/// What vulnerability scanning (phase P7) needs from a format, in the repository of the request: how its artifacts are checked,
/// what is stored (to scan on a schedule), and for an OSV format the components an artifact consists of (itself and its
/// declared dependencies, by exact version where the format states one).
/// </summary>
public interface IFormatScanner : IFormatService
{
    /// <summary>How the format's artifacts are checked. Default: components against OSV.</summary>
    ScanEngine Engine => ScanEngine.Osv;

    /// <summary>The components of the stored version, or none when it is unknown (<see cref="ScanEngine.Osv"/> formats).</summary>
    Task<IReadOnlyList<ScanComponent>> ListComponentsAsync(string name, string version, CancellationToken cancellationToken);

    /// <summary>
    /// For <see cref="ScanEngine.Image"/>: the image as a registry names it below the repository, <c>{image}@{digest}</c>, or null
    /// when it is not stored.
    /// </summary>
    Task<string?> GetImageReferenceAsync(string name, string version, CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);

    /// <summary>Every artifact version stored in the repository (what a scheduled sweep and <em>Scan now</em> go through).</summary>
    Task<IReadOnlyList<ScanArtifact>> ListArtifactsAsync(CancellationToken cancellationToken);
}
