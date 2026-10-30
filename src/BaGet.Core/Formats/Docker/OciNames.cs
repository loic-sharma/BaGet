using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BaGet.Core;

/// <summary>What an OCI route (the part of the path after the BaGet repository) addresses.</summary>
public enum OciPathKind
{
    /// <summary><c>{image}/blobs/{digest}</c>.</summary>
    Blob,

    /// <summary><c>{image}/blobs/uploads/</c>: a new upload session.</summary>
    UploadStart,

    /// <summary><c>{image}/blobs/uploads/{reference}</c>.</summary>
    Upload,

    /// <summary><c>{image}/manifests/{reference}</c> (a tag or a digest).</summary>
    Manifest,

    /// <summary><c>{image}/tags/list</c>.</summary>
    TagList,

    /// <summary><c>{image}/referrers/{digest}</c>.</summary>
    Referrers,
}

/// <summary>An OCI request path split into the image and what is asked of it.</summary>
/// <param name="Kind">What the path addresses.</param>
/// <param name="Image">The image name inside the BaGet repository (<c>team/app</c>), valid per <see cref="OciNames.IsValidImage"/>.</param>
/// <param name="Reference">The digest, tag or upload reference; null for <see cref="OciPathKind.UploadStart"/> and <see cref="OciPathKind.TagList"/>.</param>
public sealed record OciPath(OciPathKind Kind, string Image, string? Reference);

/// <summary>
/// The one place that validates and parses OCI names: image names, tags, digests, and the request path of the Distribution
/// Spec (<c>{name}/blobs/{digest}</c>, <c>{name}/blobs/uploads/{ref}</c>, <c>{name}/manifests/{ref}</c>, <c>{name}/tags/list</c>,
/// <c>{name}/referrers/{digest}</c>), where the name has any number of slashes and so can only be found from the right.
/// Image names are lower case, as the specification has them, which is also what keeps two spellings out of storage and
/// the access patterns.
/// </summary>
public static class OciNames
{
    /// <summary>The longest image name (the column is this wide).</summary>
    public const int MaxImageLength = 200;

    /// <summary>The longest tag, per the specification.</summary>
    public const int MaxTagLength = 128;

    /// <summary>The longest media type or artifact type the columns hold.</summary>
    public const int MaxMediaTypeLength = 255;

    /// <summary>The longest digest the columns hold.</summary>
    public const int MaxDigestLength = 140;

    /// <summary>The storage folder of a Docker repository below the repository's prefix.</summary>
    public const string StorageFolder = "docker";

    /// <summary>The digest algorithm this registry stores.</summary>
    public const string Sha256 = "sha256";

    private static readonly Regex ComponentPattern = new("^[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TagPattern = new("^[a-zA-Z0-9_][a-zA-Z0-9._-]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DigestPattern = new("^sha256:[a-f0-9]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex AnyDigestPattern = new("^[a-z0-9]+(?:[.+_-][a-z0-9]+)*:[a-zA-Z0-9=_-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UploadReferencePattern = new("^[A-Za-z0-9_-]{1,128}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The media types of a single-image manifest this registry stores.</summary>
    public static readonly IReadOnlyList<string> ImageManifestTypes = new[]
    {
        "application/vnd.oci.image.manifest.v1+json",
        "application/vnd.docker.distribution.manifest.v2+json",
    };

    /// <summary>The media types of a multi-platform index this registry stores.</summary>
    public static readonly IReadOnlyList<string> IndexTypes = new[]
    {
        "application/vnd.oci.image.index.v1+json",
        "application/vnd.docker.distribution.manifest.list.v2+json",
    };

    /// <summary>The media type of the empty descriptor of OCI 1.1 (<c>{}</c>, two bytes).</summary>
    public const string EmptyMediaType = "application/vnd.oci.empty.v1+json";

    /// <summary>The OCI index media type referrers answer with.</summary>
    public const string OciIndexType = "application/vnd.oci.image.index.v1+json";

    /// <summary>Whether <paramref name="mediaType"/> is a manifest type this registry stores.</summary>
    public static bool IsSupportedManifestType(string? mediaType)
        => mediaType != null && (ImageManifestTypes.Contains(mediaType, StringComparer.Ordinal) || IndexTypes.Contains(mediaType, StringComparer.Ordinal));

    /// <summary>Whether <paramref name="mediaType"/> is an index (a list of manifests).</summary>
    public static bool IsIndex(string? mediaType) => mediaType != null && IndexTypes.Contains(mediaType, StringComparer.Ordinal);

    /// <summary>
    /// Whether <paramref name="name"/> is an image name: one or more lower-case components separated by slashes, each
    /// <c>[a-z0-9]+</c> joined by <c>.</c>, <c>_</c>, <c>__</c> or dashes; at most <see cref="MaxImageLength"/> characters.
    /// </summary>
    public static bool IsValidImage(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxImageLength) return false;

        return name.Split('/').All(c => ComponentPattern.IsMatch(c));
    }

    /// <summary>Whether <paramref name="tag"/> is a tag: <c>[A-Za-z0-9_][A-Za-z0-9._-]{0,127}</c>.</summary>
    public static bool IsValidTag(string? tag) => tag != null && TagPattern.IsMatch(tag);

    /// <summary>Whether <paramref name="digest"/> is a <c>sha256:</c> digest with 64 lower-case hex digits.</summary>
    public static bool IsValidDigest(string? digest) => digest != null && digest.Length <= MaxDigestLength && DigestPattern.IsMatch(digest);

    /// <summary>
    /// Whether <paramref name="digest"/> has the shape of any digest of the specification (<c>algorithm:encoded</c>), supported
    /// or not: used to tell a digest this registry does not store from something that is no digest at all.
    /// </summary>
    public static bool LooksLikeDigest(string? digest) => digest != null && digest.Length <= 256 && AnyDigestPattern.IsMatch(digest);

    /// <summary>The digest of the hex SHA-256 <paramref name="hex"/>.</summary>
    public static string DigestOf(string hex) => Sha256 + ":" + hex;

    /// <summary>The hex part of a <c>sha256:</c> digest.</summary>
    public static string HexOf(string digest) => digest.Substring(Sha256.Length + 1);

    /// <summary>
    /// Where the blob with <paramref name="digest"/> is stored, below the repository's prefix:
    /// <c>docker/blobs/sha256/ab/abcdef…</c> (two-digit fan-out so a folder never holds millions of entries).
    /// </summary>
    public static string BlobPath(string digest)
    {
        if (!IsValidDigest(digest)) throw new ArgumentException("Not a sha256 digest.", nameof(digest));

        var hex = HexOf(digest);

        return $"{StorageFolder}/blobs/{Sha256}/{hex.Substring(0, 2)}/{hex}";
    }

    /// <summary>
    /// Whether <paramref name="reference"/> names an upload session: the opaque, URL-safe id the registry handed out.
    /// </summary>
    public static bool IsValidUploadReference(string? reference) => reference != null && UploadReferencePattern.IsMatch(reference);

    /// <summary>
    /// Splits the part of an OCI path that follows the BaGet repository (<c>team/app/blobs/sha256:…</c>) into what it
    /// addresses, or returns null when it is not a path of the specification or names an invalid image, tag or digest.
    /// The image name is found from the right because it may contain any number of slashes.
    /// </summary>
    public static OciPath? Parse(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        var segments = path.Split('/');
        if (segments.Length < 3) return null;

        // {name}/tags/list
        if (segments[^2] == "tags" && segments[^1] == "list")
        {
            return Build(OciPathKind.TagList, segments, 2, reference: null);
        }

        // {name}/blobs/uploads/ (a trailing slash gives an empty last segment) and {name}/blobs/uploads/{ref}
        if (segments.Length >= 4 && segments[^3] == "blobs" && segments[^2] == "uploads")
        {
            if (segments[^1].Length == 0) return Build(OciPathKind.UploadStart, segments, 3, reference: null);

            return IsValidUploadReference(segments[^1]) ? Build(OciPathKind.Upload, segments, 3, segments[^1]) : null;
        }

        // Some clients leave out the trailing slash of the start URL.
        if (segments[^2] == "blobs" && segments[^1] == "uploads")
        {
            return Build(OciPathKind.UploadStart, segments, 2, reference: null);
        }

        var last = segments[^1];
        switch (segments[^2])
        {
            case "blobs":
                return LooksLikeDigest(last) ? Build(OciPathKind.Blob, segments, 2, last) : null;

            case "manifests":
                // Any reference is accepted here: a read of one that is neither a tag nor a digest is "unknown" (404), as the
                // specification's conformance suite expects, and only a PUT refuses it (TAG_INVALID).
                return last.Length is > 0 and <= 300 ? Build(OciPathKind.Manifest, segments, 2, last) : null;

            case "referrers":
                return LooksLikeDigest(last) ? Build(OciPathKind.Referrers, segments, 2, last) : null;

            default:
                return null;
        }
    }

    private static OciPath? Build(OciPathKind kind, string[] segments, int tail, string? reference)
    {
        var image = string.Join("/", segments.Take(segments.Length - tail));

        return IsValidImage(image) ? new OciPath(kind, image, reference) : null;
    }
}
