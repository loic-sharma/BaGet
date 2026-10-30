using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>The error codes of the OCI Distribution Spec (and the HTTP status each one is answered with).</summary>
public enum OciErrorCode
{
    /// <summary>The blob is unknown to the image (404).</summary>
    BlobUnknown,

    /// <summary>The blob upload is invalid (400).</summary>
    BlobUploadInvalid,

    /// <summary>The upload session is unknown (404).</summary>
    BlobUploadUnknown,

    /// <summary>The provided digest did not match the uploaded content, or is not a digest this registry stores (400).</summary>
    DigestInvalid,

    /// <summary>A manifest references a blob (or manifest) the image does not hold (400).</summary>
    ManifestBlobUnknown,

    /// <summary>The manifest is not valid (400).</summary>
    ManifestInvalid,

    /// <summary>The manifest is unknown to the image (404).</summary>
    ManifestUnknown,

    /// <summary>The image name is not valid (400).</summary>
    NameInvalid,

    /// <summary>The image (or the repository) is unknown (404).</summary>
    NameUnknown,

    /// <summary>The content length does not match what was announced (400).</summary>
    SizeInvalid,

    /// <summary>The tag is not valid (400).</summary>
    TagInvalid,

    /// <summary>The operation is not supported, here or for this object (405).</summary>
    Unsupported,

    /// <summary>The chunk does not start where the upload ends (416).</summary>
    RangeInvalid,

    /// <summary>The pagination parameter is not valid (400).</summary>
    PaginationNumberInvalid,

    /// <summary>The manifest is larger than <see cref="OciOptions.MaxManifestBytes"/> (413).</summary>
    ManifestTooLarge,

    /// <summary>The repository does not take this change (a remote repository caches an upstream, a virtual one has no deploy repository) (403).</summary>
    Denied,

    /// <summary>The upstream registry of a remote repository could not be reached or sent something unusable, and nothing stored can answer (502).</summary>
    UpstreamUnavailable,
}

/// <summary>
/// A protocol-level refusal of the registry: what the client did wrong, with the code the Distribution Spec names for it. The
/// web layer answers it as the JSON error body; the pages show the message. Never thrown for the maintenance gate (that is
/// <see cref="MaintenanceException"/>, answered 503 by the middleware).
/// </summary>
public sealed class OciException : Exception
{
    /// <param name="code">The code.</param>
    /// <param name="message">A sentence for the person or client.</param>
    /// <param name="detail">A short machine-readable detail (a digest, a length), or null.</param>
    public OciException(OciErrorCode code, string message, string? detail = null)
        : base(message)
    {
        Code = code;
        Detail = detail;
    }

    /// <summary>The code.</summary>
    public OciErrorCode Code { get; }

    /// <summary>What the error is about (a digest, the length an upload holds), or null.</summary>
    public string? Detail { get; }

    /// <summary>For <see cref="OciErrorCode.RangeInvalid"/>: the number of bytes the upload holds.</summary>
    public long? UploadLength { get; init; }
}

/// <summary>A blob being served (whole or a range). Dispose it when the response is written.</summary>
public sealed class OciBlobContent : IDisposable
{
    internal OciBlobContent(Stream content, string digest, long totalSize, long offset, long length)
    {
        Content = content;
        Digest = digest;
        TotalSize = totalSize;
        Offset = offset;
        Length = length;
    }

    /// <summary>The bytes of the range.</summary>
    public Stream Content { get; }

    /// <summary>The blob's digest.</summary>
    public string Digest { get; }

    /// <summary>The blob's size.</summary>
    public long TotalSize { get; }

    /// <summary>Where the range starts.</summary>
    public long Offset { get; }

    /// <summary>The number of bytes in the range.</summary>
    public long Length { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        Content.Dispose();
        Owner?.Dispose();
    }

    /// <summary>
    /// Something to release after <see cref="Content"/> (the scope of the virtual repository's member that serves the blob, whose
    /// services a caching stream still uses while it is read).
    /// </summary>
    internal IDisposable? Owner { get; set; }
}

/// <summary>A blob as a <c>HEAD</c> reports it.</summary>
/// <param name="Digest">The digest.</param>
/// <param name="Size">The size in bytes.</param>
public sealed record OciBlobInfo(string Digest, long Size);

/// <summary>A stored manifest or index.</summary>
/// <param name="Digest">Its digest.</param>
/// <param name="MediaType">Its media type.</param>
/// <param name="Content">The bytes exactly as pushed.</param>
/// <param name="SubjectDigest">The digest it refers to (OCI 1.1), or null.</param>
/// <param name="ArtifactType">Its artifact type, or null.</param>
/// <param name="Created">When it was first pushed to the image (UTC).</param>
public sealed record OciManifestContent(string Digest, string MediaType, byte[] Content, string? SubjectDigest, string? ArtifactType, DateTime Created);

/// <summary>The result of storing a manifest.</summary>
/// <param name="Digest">The manifest's digest.</param>
/// <param name="SubjectDigest">The digest of its subject, or null.</param>
/// <param name="Created">Whether the manifest was new to the image (a re-push of the same bytes is not).</param>
/// <param name="PreviousDigest">The digest the pushed tag pointed to before this push (null for a push by digest or a new tag); lets a caller that refuses the push put the tag back.</param>
public sealed record OciManifestPut(string Digest, string? SubjectDigest, bool Created, string? PreviousDigest = null);

/// <summary>An open upload session.</summary>
/// <param name="Reference">The reference to put in the upload URL.</param>
/// <param name="Length">The bytes received so far.</param>
public sealed record OciUploadState(string Reference, long Length);

/// <summary>The result of finishing a blob upload.</summary>
/// <param name="Digest">The blob's digest.</param>
/// <param name="Size">The blob's size.</param>
public sealed record OciBlobStored(string Digest, long Size);

/// <summary>A page of tags (<c>tags/list</c>).</summary>
/// <param name="Image">The image.</param>
/// <param name="Tags">The tags, in ordinal order.</param>
/// <param name="HasMore">Whether more tags follow the last one.</param>
public sealed record OciTagList(string Image, IReadOnlyList<string> Tags, bool HasMore);

/// <summary>A descriptor in a referrers index.</summary>
/// <param name="MediaType">The referrer's media type.</param>
/// <param name="Digest">Its digest.</param>
/// <param name="Size">Its size.</param>
/// <param name="ArtifactType">Its artifact type, or null.</param>
/// <param name="Annotations">Its annotations, or null.</param>
public sealed record OciDescriptor(string MediaType, string Digest, long Size, string? ArtifactType, IReadOnlyDictionary<string, string>? Annotations);

/// <summary>An image as the browse page lists it.</summary>
/// <param name="Name">The image name.</param>
/// <param name="Tags">Number of tags.</param>
/// <param name="Manifests">Number of manifests (including indexes).</param>
/// <param name="Size">Bytes of the distinct blobs the image holds.</param>
/// <param name="Modified">The last manifest or tag change (UTC).</param>
public sealed record OciImageSummary(string Name, int Tags, int Manifests, long Size, DateTime Modified);

/// <summary>A page of <see cref="OciImageSummary"/>.</summary>
/// <param name="Total">All images that matched and may be read.</param>
/// <param name="Images">The page.</param>
public sealed record OciImagePage(int Total, IReadOnlyList<OciImageSummary> Images);

/// <summary>A layer, config or other blob a manifest lists.</summary>
/// <param name="Digest">The digest.</param>
/// <param name="Size">The size announced by the manifest.</param>
/// <param name="MediaType">The media type.</param>
public sealed record OciLayerInfo(string Digest, long Size, string MediaType);

/// <summary>A platform entry of an index.</summary>
/// <param name="Digest">The child manifest.</param>
/// <param name="MediaType">The child's media type.</param>
/// <param name="Size">The child's size.</param>
/// <param name="Os">The operating system, or null.</param>
/// <param name="Architecture">The architecture, or null.</param>
/// <param name="Variant">The variant (<c>v7</c>), or null.</param>
/// <param name="Present">Whether the child manifest is in this image.</param>
public sealed record OciPlatformInfo(string Digest, string MediaType, long Size, string? Os, string? Architecture, string? Variant, bool Present)
{
    /// <summary>The platform as one string: <c>linux/arm64/v8</c> (or <c>unknown</c> for an entry with none).</summary>
    public string Platform => Os == null && Architecture == null ? "unknown" : string.Join("/", new[] { Os, Architecture, Variant }.Where(v => !string.IsNullOrEmpty(v)).Select(v => v!));
}

/// <summary>A manifest as the image page lists it.</summary>
/// <param name="Digest">Its digest.</param>
/// <param name="MediaType">Its media type.</param>
/// <param name="Size">Bytes of the manifest document.</param>
/// <param name="IsIndex">Whether it is an index of platform manifests.</param>
/// <param name="Created">When it was first pushed to the image (UTC).</param>
/// <param name="Tags">The tags that point at it.</param>
/// <param name="ArtifactType">Its artifact type, or null.</param>
/// <param name="SubjectDigest">The digest it refers to, or null.</param>
/// <param name="Platform">For a single-image manifest, its platform when the config says so (<c>linux/amd64</c>); otherwise null.</param>
/// <param name="LayerCount">Number of layers (0 for an index).</param>
/// <param name="ContentSize">Bytes of the config and layers (the children are not summed for an index).</param>
public sealed record OciManifestSummary(
    string Digest,
    string MediaType,
    long Size,
    bool IsIndex,
    DateTime Created,
    IReadOnlyList<string> Tags,
    string? ArtifactType,
    string? SubjectDigest,
    string? Platform,
    int LayerCount,
    long ContentSize);

/// <summary>A tag of an image.</summary>
/// <param name="Name">The tag.</param>
/// <param name="Digest">The manifest it points at.</param>
/// <param name="Updated">When it was created or moved (UTC).</param>
public sealed record OciTagInfo(string Name, string Digest, DateTime Updated);

/// <summary>An image with its tags and manifests (the image page).</summary>
/// <param name="Name">The image name.</param>
/// <param name="Created">When the first upload arrived (UTC).</param>
/// <param name="Modified">The last change (UTC).</param>
/// <param name="Tags">The tags, by name.</param>
/// <param name="Manifests">The manifests, newest first.</param>
/// <param name="Size">Bytes of the distinct blobs the image holds.</param>
/// <param name="UnreferencedBlobs">Blobs held by the image that no manifest uses.</param>
public sealed record OciImageInfo(
    string Name,
    DateTime Created,
    DateTime Modified,
    IReadOnlyList<OciTagInfo> Tags,
    IReadOnlyList<OciManifestSummary> Manifests,
    long Size,
    int UnreferencedBlobs);

/// <summary>One manifest in full (the manifest page).</summary>
/// <param name="Summary">The summary.</param>
/// <param name="Json">The manifest document, pretty-printed for display (the stored bytes are unchanged).</param>
/// <param name="Config">The config blob, or null for an index.</param>
/// <param name="Layers">The layers in order (empty for an index).</param>
/// <param name="Platforms">The entries of an index (empty for a single-image manifest).</param>
/// <param name="Annotations">The manifest's annotations.</param>
public sealed record OciManifestDetail(
    OciManifestSummary Summary,
    string Json,
    OciLayerInfo? Config,
    IReadOnlyList<OciLayerInfo> Layers,
    IReadOnlyList<OciPlatformInfo> Platforms,
    IReadOnlyDictionary<string, string> Annotations);

/// <summary>What a prune removed.</summary>
/// <param name="Blobs">Layers removed from the image.</param>
/// <param name="Bytes">Their size.</param>
public sealed record OciPruneResult(int Blobs, long Bytes);

/// <summary>
/// The operations of a Docker / OCI repository, in <see cref="IRepositoryContext.Current"/>: what the Distribution Spec routes and
/// the pages share. <b>No method authorizes</b>: the caller (the controller through <see cref="IOciAccess"/>, a page through its
/// gate) has already decided that the operation is allowed, because a registry token carries its own grants and there is no
/// principal here to ask. Protocol refusals are <see cref="OciException"/>; a closed maintenance gate is
/// <see cref="MaintenanceException"/>.
/// </summary>
public interface IOciRegistry
{
    // ---- reads (pull) ------------------------------------------------------------------------------------------------

    /// <summary>Whether the image exists.</summary>
    Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken);

    /// <summary>A blob the image holds, for a <c>HEAD</c>; null when the image does not hold it.</summary>
    Task<OciBlobInfo?> GetBlobInfoAsync(string image, string digest, CancellationToken cancellationToken);

    /// <summary>A blob the image holds, from <paramref name="offset"/> for <paramref name="length"/> bytes (to the end when null); null when absent.</summary>
    Task<OciBlobContent?> OpenBlobAsync(string image, string digest, long offset, long? length, CancellationToken cancellationToken);

    /// <summary>A manifest by tag or digest; null when the image has none.</summary>
    Task<OciManifestContent?> GetManifestAsync(string image, string reference, CancellationToken cancellationToken);

    /// <summary>The tags of an image after <paramref name="last"/>, at most <paramref name="count"/>; null when the image is unknown.</summary>
    Task<OciTagList?> ListTagsAsync(string image, int? count, string? last, CancellationToken cancellationToken);

    /// <summary>The manifests of the image whose subject is <paramref name="digest"/>, optionally of one artifact type; null when the image is unknown.</summary>
    Task<IReadOnlyList<OciDescriptor>?> GetReferrersAsync(string image, string digest, string? artifactType, CancellationToken cancellationToken);

    /// <summary>The state of an upload session (what a client resumes from); null when there is none.</summary>
    Task<OciUploadState?> GetUploadAsync(string image, string reference, CancellationToken cancellationToken);

    // ---- uploads and manifests (push) --------------------------------------------------------------------------------

    /// <summary>Starts an upload session for the image (creating the image).</summary>
    Task<OciUploadState> StartUploadAsync(string image, CancellationToken cancellationToken);

    /// <summary>
    /// Appends a chunk. <paramref name="offset"/> is where the client says the chunk starts (the <c>Content-Range</c> start),
    /// or null to append at the end.
    /// </summary>
    /// <exception cref="OciException"><see cref="OciErrorCode.BlobUploadUnknown"/>, or <see cref="OciErrorCode.RangeInvalid"/> with <see cref="OciException.UploadLength"/>.</exception>
    Task<OciUploadState> AppendAsync(string image, string reference, long? offset, Stream chunk, CancellationToken cancellationToken);

    /// <summary>Appends an optional last chunk, verifies the digest, stores the blob and closes the session.</summary>
    Task<OciBlobStored> CompleteUploadAsync(string image, string reference, string digest, long? offset, Stream? lastChunk, CancellationToken cancellationToken);

    /// <summary>Discards an upload session; false when there was none.</summary>
    Task<bool> CancelUploadAsync(string image, string reference, CancellationToken cancellationToken);

    /// <summary>Stores a whole blob sent in one request (<c>POST …/uploads/?digest=</c>).</summary>
    Task<OciBlobStored> PutBlobAsync(string image, string digest, Stream content, CancellationToken cancellationToken);

    /// <summary>
    /// Makes a blob of <paramref name="fromImage"/> available in <paramref name="image"/> without copying it (cross-repository
    /// mount); false when <paramref name="fromImage"/> (in this repository) does not hold it.
    /// </summary>
    Task<bool> MountBlobAsync(string image, string digest, string fromImage, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a manifest or index, under a tag or by digest, after checking that everything it references is in the image.
    /// </summary>
    Task<OciManifestPut> PutManifestAsync(string image, string reference, string? mediaType, byte[] content, CancellationToken cancellationToken);

    // ---- deletes -----------------------------------------------------------------------------------------------------

    /// <summary>Deletes a manifest by digest, with its tags; false when absent.</summary>
    Task<bool> DeleteManifestAsync(string image, string digest, CancellationToken cancellationToken);

    /// <summary>Deletes a tag (not its manifest); false when absent.</summary>
    Task<bool> DeleteTagAsync(string image, string tag, CancellationToken cancellationToken);

    /// <summary>Removes a blob from the image; false when absent. Refused (<see cref="OciErrorCode.Unsupported"/>) while a manifest of the image uses it.</summary>
    Task<bool> DeleteBlobAsync(string image, string digest, CancellationToken cancellationToken);

    /// <summary>
    /// Where a manifest would come from, without storing or serving it: <see cref="ResolutionOutcome.Stored"/> when this repository
    /// holds the tag or digest, otherwise (a remote repository) whether its upstream has it. The resolution preview of a virtual
    /// repository asks each member.
    /// </summary>
    Task<ResolutionOutcome> ProbeAsync(string image, string reference, CancellationToken cancellationToken);

    /// <summary>Deletes the image with everything in it; false when absent.</summary>
    Task<bool> DeleteImageAsync(string image, CancellationToken cancellationToken);

    /// <summary>Removes the blobs of the image that no manifest uses and that are older than <see cref="OciOptions.PruneGrace"/>.</summary>
    Task<OciPruneResult> PruneAsync(string image, CancellationToken cancellationToken);

    // ---- the pages ---------------------------------------------------------------------------------------------------

    /// <summary>The images whose name contains <paramref name="text"/> (all when empty) that <paramref name="canRead"/> admits.</summary>
    Task<OciImagePage> ListImagesAsync(string? text, int skip, int take, Func<string, CancellationToken, Task<bool>>? canRead, CancellationToken cancellationToken);

    /// <summary>An image with its tags and manifests; null when unknown.</summary>
    Task<OciImageInfo?> GetImageAsync(string image, CancellationToken cancellationToken);

    /// <summary>A manifest in full; null when unknown.</summary>
    Task<OciManifestDetail?> GetManifestDetailAsync(string image, string digest, CancellationToken cancellationToken);
}
