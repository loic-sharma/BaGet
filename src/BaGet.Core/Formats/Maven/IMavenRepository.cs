using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>How an operation of a Maven repository ended; the HTTP routes and the pages map it the same way.</summary>
public enum MavenStatus
{
    /// <summary>Done.</summary>
    Ok,

    /// <summary>Done, and something was stored (HTTP 201).</summary>
    Created,

    /// <summary>Nobody is authenticated (HTTP 401).</summary>
    Unauthorized,

    /// <summary>The caller is not allowed, or the repository takes no changes (HTTP 403).</summary>
    Forbidden,

    /// <summary>No such artifact, version or file (HTTP 404).</summary>
    NotFound,

    /// <summary>The request clashes with what exists or with the repository's policy (HTTP 409).</summary>
    Conflict,

    /// <summary>The request is not valid (HTTP 400); <see cref="MavenResult.Message"/> says why.</summary>
    Invalid,

    /// <summary>The file is larger than <c>Maven:MaxFileBytes</c> (HTTP 413).</summary>
    TooLarge,
}

/// <summary>The outcome of an operation of a Maven repository.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Message">A sentence for the person or client when it did not succeed (or a note when it did).</param>
/// <param name="Count">For a deletion or a cleanup: how many files were removed.</param>
/// <param name="Subject">The artifact the operation was about (<c>group:artifact</c>), when it succeeded.</param>
public sealed record MavenResult(MavenStatus Status, string? Message = null, int Count = 0, string? Subject = null)
{
    /// <summary>Whether the operation was done.</summary>
    public bool Succeeded => Status is MavenStatus.Ok or MavenStatus.Created;

    internal static MavenResult Ok(string? message = null, int count = 0, string? subject = null) => new(MavenStatus.Ok, message, count, subject);

    internal static MavenResult Created(string? message = null, string? subject = null) => new(MavenStatus.Created, message, 0, subject);

    internal static MavenResult Fail(MavenStatus status, string message) => new(status, message);
}

/// <summary>A file (or generated metadata) being served. Dispose it when the response is written.</summary>
public sealed class MavenDownload : IDisposable
{
    internal MavenDownload(Stream content, long length, string contentType, string fileName, DateTime lastModified)
    {
        Content = content;
        Length = length;
        ContentType = contentType;
        FileName = fileName;
        LastModified = lastModified;
    }

    /// <summary>The bytes.</summary>
    public Stream Content { get; }

    /// <summary>Their number.</summary>
    public long Length { get; }

    /// <summary>The content type.</summary>
    public string ContentType { get; }

    /// <summary>The file name.</summary>
    public string FileName { get; }

    /// <summary>When the file was stored (UTC).</summary>
    public DateTime LastModified { get; }

    /// <summary>SHA-1 hex of the content, sent as <c>X-Checksum-Sha1</c>.</summary>
    public string? Sha1 { get; internal init; }

    /// <summary>MD5 hex of the content.</summary>
    public string? Md5 { get; internal init; }

    /// <summary>SHA-256 hex of the content.</summary>
    public string? Sha256 { get; internal init; }

    /// <inheritdoc/>
    public void Dispose() => Content.Dispose();
}

/// <summary>A file of a version as the pages list it.</summary>
/// <param name="FileName">The file name.</param>
/// <param name="Classifier">The classifier, or null.</param>
/// <param name="Extension">The extension.</param>
/// <param name="Size">Bytes.</param>
/// <param name="Sha1">SHA-1 hex.</param>
/// <param name="Md5">MD5 hex.</param>
/// <param name="Sha256">SHA-256 hex.</param>
/// <param name="Sha512">SHA-512 hex.</param>
/// <param name="Created">When stored (UTC).</param>
/// <param name="SnapshotTimestamp">The snapshot build timestamp, or null.</param>
/// <param name="SnapshotBuild">The snapshot build number, or 0.</param>
public sealed record MavenFileDetail(
    string FileName,
    string? Classifier,
    string Extension,
    long Size,
    string Sha1,
    string Md5,
    string Sha256,
    string Sha512,
    DateTime Created,
    string? SnapshotTimestamp,
    int SnapshotBuild);

/// <summary>A version as the artifact page lists it.</summary>
/// <param name="Version">The version.</param>
/// <param name="IsSnapshot">Whether it is a snapshot.</param>
/// <param name="Name">The POM's name, or null.</param>
/// <param name="Description">The POM's description, or null.</param>
/// <param name="Packaging">The POM's packaging, or null.</param>
/// <param name="Created">The first file (UTC).</param>
/// <param name="Modified">The last change (UTC).</param>
/// <param name="FileCount">Files of the version.</param>
/// <param name="Size">Bytes of the version.</param>
/// <param name="Builds">For a snapshot: how many timestamped builds it holds.</param>
public sealed record MavenVersionSummary(
    string Version,
    bool IsSnapshot,
    string? Name,
    string? Description,
    string? Packaging,
    DateTime Created,
    DateTime Modified,
    int FileCount,
    long Size,
    int Builds);

/// <summary>An artifact with its versions, newest first.</summary>
/// <param name="GroupId">The group id.</param>
/// <param name="ArtifactId">The artifact id.</param>
/// <param name="Created">The first deploy (UTC).</param>
/// <param name="Modified">The last change (UTC).</param>
/// <param name="Versions">Its versions, highest first.</param>
public sealed record MavenArtifactInfo(string GroupId, string ArtifactId, DateTime Created, DateTime Modified, IReadOnlyList<MavenVersionSummary> Versions)
{
    /// <summary>The highest release, else the highest version.</summary>
    public MavenVersionSummary? Latest => Versions.Count == 0 ? null : Versions.FirstOrDefault(v => !v.IsSnapshot) ?? Versions[0];

    /// <summary><c>group:artifact</c>.</summary>
    public string Name => MavenNames.ArtifactName(GroupId, ArtifactId);
}

/// <summary>One version with its files and POM.</summary>
/// <param name="Summary">The version.</param>
/// <param name="Files">Its files, by name.</param>
/// <param name="Pom">The parsed POM, or null when none was deployed or it could not be read.</param>
/// <param name="PomText">The POM text (for the view), or null.</param>
public sealed record MavenVersionDetail(MavenVersionSummary Summary, IReadOnlyList<MavenFileDetail> Files, MavenPomInfo? Pom, string? PomText);

/// <summary>A search hit.</summary>
/// <param name="GroupId">The group id.</param>
/// <param name="ArtifactId">The artifact id.</param>
/// <param name="Version">The newest version (a release if there is one), or null.</param>
/// <param name="Description">The POM's description of it, or null.</param>
/// <param name="Name">The POM's name of it, or null.</param>
/// <param name="Modified">The last change (UTC).</param>
public sealed record MavenSearchHit(string GroupId, string ArtifactId, string? Version, string? Name, string? Description, DateTime Modified)
{
    /// <summary><c>group:artifact</c>.</summary>
    public string CoordinateName => MavenNames.ArtifactName(GroupId, ArtifactId);
}

/// <summary>A page of hits.</summary>
/// <param name="Total">All matches.</param>
/// <param name="Hits">This page.</param>
public sealed record MavenSearchPage(int Total, IReadOnlyList<MavenSearchHit> Hits);

/// <summary>
/// A Maven repository (<see cref="RepositoryFormat.Maven"/>): the Maven 2 layout under <c>maven/</c> of the repository
/// base, checksums, <c>maven-metadata.xml</c>, snapshots with timestamped builds and the pages' reads. It works in the repository
/// of the request, whose kind picks the implementation (<see cref="RepositoryMavenRepository"/>): <see cref="MavenRepositoryService"/>
/// for a local repository (generated metadata, the release/snapshot policy; every change takes the caller and is authorized through
/// <see cref="IPackageAuthorizationService"/>: the access model, then ownership of <c>group:artifact</c>), <see cref="RemoteMavenRepository"/>
/// (a caching proxy of an upstream such as Maven Central, which takes no changes) and <see cref="VirtualMavenRepository"/> (members
/// merged, changes going to the deploy repository), so routes, pages and accounts cannot differ.
/// </summary>
public interface IMavenRepository
{
    /// <summary>
    /// The artifact a request path is about, for the access check: the version level of a metadata path when that version is
    /// stored, else the artifact level. Null when the path names no artifact (a group-level metadata path).
    /// </summary>
    Task<MavenCoordinate?> ResolveAsync(MavenRequestPath path, CancellationToken cancellationToken);

    /// <summary>Opens what <paramref name="path"/> names: a stored file, a checksum, or generated metadata. Null when there is none.</summary>
    Task<MavenDownload?> OpenAsync(MavenRequestPath path, CancellationToken cancellationToken);

    /// <summary>
    /// Like <see cref="OpenAsync"/> for a <c>HEAD</c> request: the answer's length, type and checksums are right but its content is
    /// not read (a remote repository does not download and keep a file only to say that it exists). Null when there is none.
    /// </summary>
    Task<MavenDownload?> HeadAsync(MavenRequestPath path, CancellationToken cancellationToken);

    /// <summary>
    /// <c>PUT</c>: stores a file, checks a checksum, or accepts metadata (which is generated, not stored). The body is read once,
    /// to a temporary file, while its checksums are computed.
    /// </summary>
    Task<MavenResult> PutAsync(BaGetPrincipal? caller, MavenRequestPath path, Stream content, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the files of one artifact version at once (the upload page): a <c>pom</c> (generated when none is given) and the
    /// main file, each under its Maven name.
    /// </summary>
    Task<MavenResult> DeployAsync(BaGetPrincipal? caller, MavenDeployment deployment, CancellationToken cancellationToken);

    /// <summary><c>DELETE</c> of one file (and its checksums with it). The version and artifact go with their last file.</summary>
    Task<MavenResult> DeleteFileAsync(BaGetPrincipal? caller, MavenCoordinate version, string fileName, CancellationToken cancellationToken);

    /// <summary>Deletes a version with its files.</summary>
    Task<MavenResult> DeleteVersionAsync(BaGetPrincipal? caller, MavenCoordinate version, CancellationToken cancellationToken);

    /// <summary>Deletes an artifact with every version.</summary>
    Task<MavenResult> DeleteArtifactAsync(BaGetPrincipal? caller, string groupId, string artifactId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the timestamped builds of a snapshot version older than the newest <paramref name="keep"/> (at least 1). The
    /// result's <see cref="MavenResult.Count"/> is the number of files removed.
    /// </summary>
    Task<MavenResult> CleanupSnapshotsAsync(BaGetPrincipal? caller, MavenCoordinate version, int keep, CancellationToken cancellationToken);

    /// <summary>The artifacts whose group id, artifact id or POM name or description contains the text, by coordinates.</summary>
    /// <param name="text">The text, or null for everything.</param>
    /// <param name="skip">Hits to skip.</param>
    /// <param name="take">Hits to return.</param>
    /// <param name="canRead">Whether the caller may read an artifact (<c>group:artifact</c>); null for all. Unreadable ones are left out of the page and the total.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<MavenSearchPage> SearchAsync(string? text, int skip, int take, Func<string, CancellationToken, Task<bool>>? canRead, CancellationToken cancellationToken);

    /// <summary>An artifact with its versions, or null.</summary>
    Task<MavenArtifactInfo?> GetArtifactAsync(string groupId, string artifactId, CancellationToken cancellationToken);

    /// <summary>One version with its files and POM, or null.</summary>
    Task<MavenVersionDetail?> GetVersionAsync(string groupId, string artifactId, string version, CancellationToken cancellationToken);

    /// <summary>The settings of the repository (policy, redeploy, snapshot builds kept; a remote or virtual repository has none of its own and accepts both kinds).</summary>
    Task<MavenRepositorySettings> GetSettingsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Where a version would come from, without storing or serving anything: stored here, fetched from the upstream, unknown, or
    /// not askable (the resolution preview of a virtual repository asks each member).
    /// </summary>
    Task<ResolutionOutcome> ProbeAsync(string groupId, string artifactId, string version, CancellationToken cancellationToken);
}

/// <summary>What the upload page hands to <see cref="IMavenRepository.DeployAsync"/>.</summary>
public sealed class MavenDeployment
{
    /// <summary>The group id.</summary>
    public string? GroupId { get; init; }

    /// <summary>The artifact id.</summary>
    public string? ArtifactId { get; init; }

    /// <summary>The version.</summary>
    public string? Version { get; init; }

    /// <summary>The packaging, the extension of the main file when it has no other (default <c>jar</c>).</summary>
    public string? Packaging { get; init; }

    /// <summary>The classifier of the main file, or null.</summary>
    public string? Classifier { get; init; }

    /// <summary>The extension of the main file (default: the packaging).</summary>
    public string? Extension { get; init; }

    /// <summary>The main file, or null when only a POM is deployed.</summary>
    public Stream? File { get; init; }

    /// <summary>The POM file, or null to generate a minimal one (only when the main file has no classifier).</summary>
    public Stream? Pom { get; init; }
}

/// <summary>
/// The upstream of a remote Maven repository could not be reached (or refused the credentials, or sent something that is not a
/// repository's answer) and nothing stored or remembered can answer instead. The HTTP route answers 502; a virtual repository
/// skips the member. The message names no URL and no credential.
/// </summary>
public sealed class MavenUpstreamException : Exception
{
    /// <param name="message">What failed, safe to show.</param>
    public MavenUpstreamException(string message)
        : base(message)
    {
    }
}
