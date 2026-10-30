using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BaGet.Tests;

/// <summary>Builds the small images (config, layers, manifest, index) that the registry tests push.</summary>
public sealed class OciTestImage
{
    public const string ManifestType = "application/vnd.oci.image.manifest.v1+json";
    public const string IndexType = "application/vnd.oci.image.index.v1+json";
    public const string ConfigType = "application/vnd.oci.image.config.v1+json";
    public const string LayerType = "application/vnd.oci.image.layer.v1.tar+gzip";

    public byte[] Config { get; init; } = Array.Empty<byte>();

    public string ConfigDigest { get; init; } = string.Empty;

    public IReadOnlyList<byte[]> Layers { get; init; } = Array.Empty<byte[]>();

    public IReadOnlyList<string> LayerDigests { get; init; } = Array.Empty<string>();

    public byte[] Manifest { get; init; } = Array.Empty<byte>();

    public string ManifestDigest { get; init; } = string.Empty;

    public static string Digest(byte[] content) => "sha256:" + Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    /// <summary>An image with <paramref name="layers"/> layers of distinct content derived from <paramref name="seed"/>.</summary>
    public static OciTestImage Create(string seed, int layers = 1, string os = "linux", string architecture = "amd64", string? subject = null, string? artifactType = null, int layerSize = 64, string configType = ConfigType)
    {
        var config = Encoding.UTF8.GetBytes($"{{\"architecture\":\"{architecture}\",\"os\":\"{os}\",\"seed\":\"{seed}\"}}");
        var blobs = Enumerable.Range(0, layers)
            .Select(i =>
            {
                var bytes = new byte[layerSize];
                new Random(HashCode(seed + i)).NextBytes(bytes);
                return bytes;
            })
            .ToList();
        var configDigest = Digest(config);
        var layerDigests = blobs.Select(Digest).ToList();

        var json = new StringBuilder("{\"schemaVersion\":2,\"mediaType\":\"" + ManifestType + "\"");
        if (artifactType != null) json.Append($",\"artifactType\":\"{artifactType}\"");
        json.Append($",\"config\":{{\"mediaType\":\"{configType}\",\"digest\":\"{configDigest}\",\"size\":{config.Length}}}");
        json.Append(",\"layers\":[");
        json.Append(string.Join(",", blobs.Select((b, i) => $"{{\"mediaType\":\"{LayerType}\",\"digest\":\"{layerDigests[i]}\",\"size\":{b.Length}}}")));
        json.Append(']');
        if (subject != null) json.Append($",\"subject\":{{\"mediaType\":\"{ManifestType}\",\"digest\":\"{subject}\",\"size\":1}}");
        json.Append('}');

        var manifest = Encoding.UTF8.GetBytes(json.ToString());

        return new OciTestImage
        {
            Config = config,
            ConfigDigest = configDigest,
            Layers = blobs,
            LayerDigests = layerDigests,
            Manifest = manifest,
            ManifestDigest = Digest(manifest),
        };
    }

    /// <summary>An index over <paramref name="children"/>, with the platform of each.</summary>
    public static (byte[] Content, string Digest) Index(params (OciTestImage Image, string Os, string Architecture, string? Variant)[] children)
    {
        var entries = children.Select(c =>
            $"{{\"mediaType\":\"{ManifestType}\",\"digest\":\"{c.Image.ManifestDigest}\",\"size\":{c.Image.Manifest.Length},\"platform\":{{\"architecture\":\"{c.Architecture}\",\"os\":\"{c.Os}\"{(c.Variant == null ? string.Empty : $",\"variant\":\"{c.Variant}\"")}}}}}");
        var content = Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"mediaType\":\"" + IndexType + "\",\"manifests\":[" + string.Join(",", entries) + "]}");

        return (content, Digest(content));
    }

    private static int HashCode(string text)
    {
        var hash = 17;
        foreach (var c in text) hash = unchecked(hash * 31 + c);
        return hash;
    }
}
