using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace BaGet.Core;

/// <summary>A descriptor of a manifest: a pointer to a blob or manifest by digest.</summary>
/// <param name="MediaType">The media type.</param>
/// <param name="Digest">The digest (<c>sha256:…</c>).</param>
/// <param name="Size">The announced size.</param>
/// <param name="Foreign">Whether it is a foreign (non-distributable) layer: a foreign or non-distributable media type with <c>urls</c>. Clients never upload these, so the registry neither requires nor counts them.</param>
public sealed record OciParsedDescriptor(string MediaType, string Digest, long Size, bool Foreign = false);

/// <summary>An entry of an index: a child manifest with the platform it is for.</summary>
/// <param name="Descriptor">The child.</param>
/// <param name="Os">The operating system, or null.</param>
/// <param name="Architecture">The architecture, or null.</param>
/// <param name="Variant">The variant, or null.</param>
public sealed record OciParsedChild(OciParsedDescriptor Descriptor, string? Os, string? Architecture, string? Variant);

/// <summary>A manifest or index understood by the registry.</summary>
public sealed class OciParsedManifest
{
    /// <summary>The media type (from the document when it carries one, else the request's).</summary>
    public string MediaType { get; init; } = string.Empty;

    /// <summary>Whether it is an index.</summary>
    public bool IsIndex { get; init; }

    /// <summary>The config of a single-image manifest; null for an index.</summary>
    public OciParsedDescriptor? Config { get; init; }

    /// <summary>The layers of a single-image manifest.</summary>
    public IReadOnlyList<OciParsedDescriptor> Layers { get; init; } = Array.Empty<OciParsedDescriptor>();

    /// <summary>The entries of an index.</summary>
    public IReadOnlyList<OciParsedChild> Children { get; init; } = Array.Empty<OciParsedChild>();

    /// <summary>The subject (OCI 1.1), or null.</summary>
    public OciParsedDescriptor? Subject { get; init; }

    /// <summary>The artifact type: the document's <c>artifactType</c>, else the config's media type (single-image manifests).</summary>
    public string? ArtifactType { get; init; }

    /// <summary>The document's <c>annotations</c>.</summary>
    public IReadOnlyDictionary<string, string> Annotations { get; init; } = new Dictionary<string, string>();

    /// <summary>The distinct blob digests the manifest uses (config and layers), foreign layers excluded (they are not stored here).</summary>
    public IEnumerable<string> BlobDigests
        => (Config == null ? Layers : Layers.Prepend(Config)).Where(d => !d.Foreign).Select(d => d.Digest).Distinct(StringComparer.Ordinal);
}

/// <summary>
/// Reads a manifest or index document (OCI image spec and the Docker v2 equivalents) far enough to enforce what a registry
/// must: it is UTF-8 JSON, schema version 2, the media type matches, every descriptor is a well-formed <c>sha256:</c> pointer.
/// It never changes the bytes (the digest is over them).
/// </summary>
public static class OciManifestParser
{
    /// <summary>
    /// Parses <paramref name="content"/>. <paramref name="mediaType"/> is the request's <c>Content-Type</c>, which must be a
    /// supported manifest type and, when the document names one, equal to it.
    /// </summary>
    /// <exception cref="OciException"><see cref="OciErrorCode.ManifestInvalid"/> with the reason.</exception>
    public static OciParsedManifest Parse(string? mediaType, byte[] content)
    {
        if (content == null) throw new ArgumentNullException(nameof(content));

        if (!OciNames.IsSupportedManifestType(mediaType))
        {
            throw new OciException(OciErrorCode.ManifestInvalid, $"The manifest media type '{mediaType}' is not supported: use {string.Join(", ", OciNames.ImageManifestTypes.Concat(OciNames.IndexTypes))}.");
        }

        string text;
        try
        {
            // A BOM, or bytes that are not valid UTF-8, would not round-trip through the stored text: the digest must stay valid.
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(content);
        }
        catch (DecoderFallbackException)
        {
            throw new OciException(OciErrorCode.ManifestInvalid, "The manifest is not valid UTF-8.");
        }

        if (!content.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(text)))
        {
            throw new OciException(OciErrorCode.ManifestInvalid, "The manifest must not start with a byte order mark.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
        }
        catch (JsonException e)
        {
            throw new OciException(OciErrorCode.ManifestInvalid, "The manifest is not valid JSON: " + e.Message);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Invalid("The manifest must be a JSON object.");

            if (!root.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version) || version != 2)
            {
                throw Invalid("schemaVersion must be 2.");
            }

            if (root.TryGetProperty("mediaType", out var declared))
            {
                if (declared.ValueKind != JsonValueKind.String || !string.Equals(declared.GetString(), mediaType, StringComparison.Ordinal))
                {
                    throw Invalid($"The mediaType of the document does not match the Content-Type '{mediaType}'.");
                }
            }

            var isIndex = OciNames.IsIndex(mediaType);
            var subject = root.TryGetProperty("subject", out var subjectElement) && subjectElement.ValueKind != JsonValueKind.Null ? Descriptor(subjectElement, "subject") : null;
            var annotations = Annotations(root);
            var explicitType = root.TryGetProperty("artifactType", out var artifact) && artifact.ValueKind == JsonValueKind.String ? artifact.GetString() : null;
            if (explicitType != null && explicitType.Length > OciNames.MaxMediaTypeLength) throw Invalid("artifactType is too long.");

            if (isIndex)
            {
                if (!root.TryGetProperty("manifests", out var manifests) || manifests.ValueKind != JsonValueKind.Array) throw Invalid("An index needs a manifests array.");

                var children = new List<OciParsedChild>();
                foreach (var entry in manifests.EnumerateArray())
                {
                    var descriptor = Descriptor(entry, "manifests[]");
                    string? os = null, architecture = null, variant = null;
                    if (entry.TryGetProperty("platform", out var platform) && platform.ValueKind == JsonValueKind.Object)
                    {
                        os = StringOf(platform, "os");
                        architecture = StringOf(platform, "architecture");
                        variant = StringOf(platform, "variant");
                    }

                    children.Add(new OciParsedChild(descriptor, os, architecture, variant));
                }

                return new OciParsedManifest
                {
                    MediaType = mediaType!,
                    IsIndex = true,
                    Children = children,
                    Subject = subject,
                    ArtifactType = explicitType,
                    Annotations = annotations,
                };
            }

            if (!root.TryGetProperty("config", out var config) || config.ValueKind != JsonValueKind.Object) throw Invalid("A manifest needs a config descriptor.");
            if (!root.TryGetProperty("layers", out var layers) || layers.ValueKind != JsonValueKind.Array) throw Invalid("A manifest needs a layers array.");

            var configDescriptor = Descriptor(config, "config");
            var layerList = layers.EnumerateArray().Select(l => Descriptor(l, "layers[]")).ToList();

            return new OciParsedManifest
            {
                MediaType = mediaType!,
                Config = configDescriptor,
                Layers = layerList,
                Subject = subject,
                ArtifactType = explicitType ?? (OciNames.ImageManifestTypes[0] == mediaType ? configDescriptor.MediaType : null),
                Annotations = annotations,
            };
        }
    }

    private static OciException Invalid(string message) => new(OciErrorCode.ManifestInvalid, message);

    private static string? StringOf(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static OciParsedDescriptor Descriptor(JsonElement element, string where)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid($"{where} must be a descriptor object.");

        var mediaType = StringOf(element, "mediaType");
        var digest = StringOf(element, "digest");
        if (string.IsNullOrEmpty(mediaType)) throw Invalid($"{where} needs a mediaType.");
        if (mediaType.Length > OciNames.MaxMediaTypeLength) throw Invalid($"{where} mediaType is too long.");
        if (digest == null || !OciNames.LooksLikeDigest(digest)) throw Invalid($"{where} needs a digest.");
        if (!OciNames.IsValidDigest(digest)) throw new OciException(OciErrorCode.DigestInvalid, $"{where} uses a digest algorithm this registry does not store (only sha256 is supported).", digest);
        if (!element.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out var bytes) || bytes < 0) throw Invalid($"{where} needs a size.");

        var foreign = (mediaType.Contains("foreign", StringComparison.OrdinalIgnoreCase) || mediaType.Contains("nondistributable", StringComparison.OrdinalIgnoreCase))
                      && element.TryGetProperty("urls", out var urls) && urls.ValueKind == JsonValueKind.Array && urls.GetArrayLength() > 0;

        return new OciParsedDescriptor(mediaType, digest, bytes, foreign);
    }

    private static IReadOnlyDictionary<string, string> Annotations(JsonElement root)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("annotations", out var annotations) && annotations.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in annotations.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String) result[property.Name] = property.Value.GetString()!;
            }
        }

        return result;
    }
}
