using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>How an operation of the npm registry ended; the HTTP routes and the pages map it the same way.</summary>
public enum NpmStatus
{
    /// <summary>Done.</summary>
    Ok,

    /// <summary>Done, and something new exists (a version was published).</summary>
    Created,

    /// <summary>Nobody is authenticated (HTTP 401).</summary>
    Unauthorized,

    /// <summary>The caller is not allowed (HTTP 403).</summary>
    Forbidden,

    /// <summary>No such package, version, tag or file (HTTP 404).</summary>
    NotFound,

    /// <summary>The request clashes with what exists, such as publishing over a published version (HTTP 409).</summary>
    Conflict,

    /// <summary>The request is not valid (HTTP 400); <see cref="NpmResult.Message"/> says why.</summary>
    Invalid,
}

/// <summary>The outcome of an operation of the registry.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Message">A sentence for the person or client when it did not succeed (or a note when it did).</param>
/// <param name="Subject">The package the operation was about, when it succeeded (the name a publish created a version of).</param>
public sealed record NpmResult(NpmStatus Status, string? Message = null, string? Subject = null)
{
    /// <summary>Whether the operation was done.</summary>
    public bool Succeeded => Status is NpmStatus.Ok or NpmStatus.Created;

    internal static NpmResult Ok(string? message = null) => new(NpmStatus.Ok, message);

    internal static NpmResult Created(string? message = null, string? subject = null) => new(NpmStatus.Created, message, subject);

    internal static NpmResult Fail(NpmStatus status, string message) => new(status, message);
}

/// <summary>One published version as stored.</summary>
/// <param name="Version">The version string as published.</param>
/// <param name="Published">When it was published.</param>
/// <param name="Deprecated">The deprecation message, or null.</param>
/// <param name="TarballSize">Bytes of the tarball.</param>
/// <param name="Integrity">The tarball's SRI string (<c>sha512-…</c>).</param>
/// <param name="Shasum">The tarball's SHA-1 in hex.</param>
/// <param name="Manifest">The version's manifest (<c>package.json</c> content and <c>dist</c>), without the tarball URL.</param>
public sealed record NpmVersionInfo(string Version, DateTime Published, string? Deprecated, long TarballSize, string Integrity, string Shasum, JsonObject Manifest)
{
    /// <summary>The manifest's <c>description</c>, or null.</summary>
    public string? Description => Manifest["description"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>The manifest's <c>readme</c>, or null.</summary>
    public string? Readme => Manifest["readme"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>The manifest's <c>license</c> when it is a string, or null.</summary>
    public string? License => Manifest["license"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>The manifest's <c>keywords</c> that are strings.</summary>
    public IReadOnlyList<string> Keywords => (Manifest["keywords"] as JsonArray)?
        .Select(k => k is JsonValue v && v.TryGetValue<string>(out var s) ? s : null)
        .Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList() ?? new List<string>();

    /// <summary>The names and ranges of an object-valued manifest field: <c>dependencies</c>, <c>peerDependencies</c>…</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Map(string field) => (Manifest[field] as JsonObject)?
        .Select(p => new KeyValuePair<string, string>(p.Key, p.Value is JsonValue v && v.TryGetValue<string>(out var s) ? s : p.Value?.ToJsonString() ?? string.Empty))
        .OrderBy(p => p.Key, StringComparer.Ordinal).ToList() ?? new List<KeyValuePair<string, string>>();
}

/// <summary>A package with its versions and dist-tags, as stored.</summary>
/// <param name="Name">The name (<c>@scope/name</c> for a scoped package).</param>
/// <param name="Created">The first publish.</param>
/// <param name="Modified">The last change.</param>
/// <param name="DistTags">Tag to version.</param>
/// <param name="Versions">Every version, oldest first by semantic version.</param>
public sealed record NpmPackageInfo(string Name, DateTime Created, DateTime Modified, IReadOnlyDictionary<string, string> DistTags, IReadOnlyList<NpmVersionInfo> Versions)
{
    /// <summary>The version <c>latest</c> points at, or the highest one when there is no such tag.</summary>
    public NpmVersionInfo? Latest
    {
        get
        {
            if (DistTags.TryGetValue("latest", out var tagged))
            {
                var found = Versions.FirstOrDefault(v => v.Version == tagged);
                if (found != null) return found;
            }

            var highest = NpmNames.Highest(Versions.Select(v => v.Version));

            return highest == null ? null : Versions.First(v => v.Version == highest);
        }
    }

    /// <summary>The document revision clients send back on a write (<c>?write=true</c> reads, <c>/-rev/{rev}</c> writes): changes with every change of the package.</summary>
    public string Revision => $"{Versions.Count}-{Modified.Ticks:x}";
}

/// <summary>One package in a search result.</summary>
/// <param name="Name">The name.</param>
/// <param name="Version">The latest version.</param>
/// <param name="Description">Its description.</param>
/// <param name="Keywords">Its keywords.</param>
/// <param name="Modified">The last change.</param>
public sealed record NpmSearchHit(string Name, string? Version, string? Description, IReadOnlyList<string> Keywords, DateTime Modified);

/// <summary>A page of packages.</summary>
/// <param name="Total">All matches.</param>
/// <param name="Hits">This page, ordered by name.</param>
public sealed record NpmSearchPage(int Total, IReadOnlyList<NpmSearchHit> Hits);

/// <summary>A tarball being served.</summary>
/// <param name="Content">The bytes; the caller disposes it.</param>
/// <param name="Length">Its size.</param>
/// <param name="Integrity">Its SRI string.</param>
public sealed record NpmTarball(Stream Content, long Length, string Integrity);

/// <summary>A version to publish.</summary>
/// <param name="Name">The package name.</param>
/// <param name="Version">The version.</param>
/// <param name="Manifest">The version's manifest as the client sent it (<c>package.json</c> plus what npm adds).</param>
/// <param name="Tag">The dist-tag to point at the version.</param>
/// <param name="Tarball">The tarball: seekable and readable; it is read from its start, and the caller disposes it.</param>
/// <param name="ExpectedIntegrity">The SRI string the client computed, or null; a mismatch refuses the publish.</param>
/// <param name="ExpectedShasum">The SHA-1 the client computed, or null; a mismatch refuses the publish.</param>
/// <param name="ExpectedLength">The size the client declared, or null; a mismatch refuses the publish.</param>
public sealed record NpmPublication(
    string Name,
    string Version,
    JsonObject Manifest,
    string Tag,
    Stream Tarball,
    string? ExpectedIntegrity = null,
    string? ExpectedShasum = null,
    long? ExpectedLength = null);

/// <summary>
/// The npm registry of the current repository (<see cref="IRepositoryContext"/>): what the HTTP routes and the pages do with
/// packages. Reads take no caller (the route or page has already required Read); every write takes the caller and applies the
/// access model and package ownership through <see cref="IPackageAuthorizationService"/>, so a route and a page cannot
/// differ in who may do what. Names are the lower-case names <see cref="NpmNames"/> accepts.
/// </summary>
public interface INpmRegistry
{
    /// <summary>The package, or null when the repository has none of that name.</summary>
    Task<NpmPackageInfo?> GetPackageAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// The packument (the registry's document for a package). With <paramref name="abbreviated"/> the install-only form
    /// (<c>application/vnd.npm.install-v1+json</c>) that clients prefer for <c>npm install</c>. Null when there is no such package.
    /// </summary>
    Task<JsonObject?> GetPackumentAsync(string name, bool abbreviated, CancellationToken cancellationToken);

    /// <summary>The tarball of a version, or null when the package, version or file does not exist.</summary>
    Task<NpmTarball?> OpenTarballAsync(string name, string file, CancellationToken cancellationToken);

    /// <summary>Publishes a version (<c>npm publish</c>). <see cref="NpmStatus.Created"/> when done.</summary>
    Task<NpmResult> PublishAsync(BaGetPrincipal? caller, NpmPublication publication, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes a tarball chosen in the browser: the name and version come from its <c>package.json</c>, the tag is
    /// <paramref name="tag"/>. The same checks as <see cref="PublishAsync"/>.
    /// </summary>
    Task<NpmResult> PublishTarballAsync(BaGetPrincipal? caller, Stream tarball, string tag, CancellationToken cancellationToken);

    /// <summary>The dist-tags of a package (tag to version), or null when there is no such package.</summary>
    Task<IReadOnlyDictionary<string, string>?> GetDistTagsAsync(string name, CancellationToken cancellationToken);

    /// <summary>Points <paramref name="tag"/> at <paramref name="version"/> (Deploy).</summary>
    Task<NpmResult> SetDistTagAsync(BaGetPrincipal? caller, string name, string tag, string version, CancellationToken cancellationToken);

    /// <summary>Removes <paramref name="tag"/> (Deploy).</summary>
    Task<NpmResult> RemoveDistTagAsync(BaGetPrincipal? caller, string name, string tag, CancellationToken cancellationToken);

    /// <summary>
    /// Sets or clears (null or empty message) the deprecation of existing versions (Annotate); versions not in the dictionary
    /// are untouched.
    /// </summary>
    Task<NpmResult> DeprecateAsync(BaGetPrincipal? caller, string name, IReadOnlyDictionary<string, string?> messages, CancellationToken cancellationToken);

    /// <summary>
    /// Unpublishes versions and deletes their tarballs. Deploy is enough for a version younger than
    /// <see cref="NpmOptions.UnpublishWindow"/>; an older one needs Delete. The dist-tags that pointed at a removed version
    /// are re-pointed with <paramref name="newTags"/> (tag to version) when it names a remaining version, else removed, and
    /// <c>latest</c> falls back to the highest remaining version. Removing the last version removes the package.
    /// </summary>
    Task<NpmResult> UnpublishVersionsAsync(
        BaGetPrincipal? caller,
        string name,
        IReadOnlyCollection<string> versions,
        IReadOnlyDictionary<string, string>? newTags,
        CancellationToken cancellationToken);

    /// <summary>Unpublishes the whole package: every version and tarball, and the package itself.</summary>
    Task<NpmResult> UnpublishPackageAsync(BaGetPrincipal? caller, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a tarball file (<c>DELETE /{name}/-/{file}/-rev/{rev}</c>, the last step of <c>npm unpublish</c>). When its
    /// version is still published this is <see cref="UnpublishVersionsAsync"/>; when the version is already gone it removes the
    /// leftover file (Deploy).
    /// </summary>
    Task<NpmResult> DeleteTarballAsync(BaGetPrincipal? caller, string name, string file, CancellationToken cancellationToken);

    /// <summary>
    /// Packages whose name contains <paramref name="text"/> (all when null or empty), ordered by name. With
    /// <paramref name="canRead"/> only the packages it accepts are listed and counted (the per-package Read of the access model:
    /// a listing must not name a package that its page would hide); without it every package of the repository is.
    /// </summary>
    Task<NpmSearchPage> SearchAsync(
        string? text,
        int skip,
        int take,
        Func<string, CancellationToken, Task<bool>>? canRead = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Where <paramref name="version"/> of <paramref name="name"/> would come from, without fetching or storing anything:
    /// <see cref="ResolutionOutcome.Stored"/> when the repository holds it, <see cref="ResolutionOutcome.WouldFetch"/> when it is a
    /// remote repository whose upstream has it, <see cref="ResolutionOutcome.Offline"/> / <see cref="ResolutionOutcome.Unavailable"/> /
    /// <see cref="ResolutionOutcome.NotFound"/> otherwise. What the resolution preview of a virtual repository asks of each member.
    /// </summary>
    Task<ResolutionOutcome> ProbeAsync(string name, string version, CancellationToken cancellationToken);
}

/// <summary>
/// The upstream of a remote npm repository could not be reached (or refused the credentials, or sent something that is not a
/// registry's answer) and nothing stored or remembered can answer instead. The HTTP route answers 502; a virtual repository
/// skips the member. The message names no URL and no credential.
/// </summary>
public sealed class NpmUpstreamException : Exception
{
    /// <param name="message">What failed, safe to show.</param>
    public NpmUpstreamException(string message)
        : base(message)
    {
    }
}
