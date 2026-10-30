using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;

namespace BaGet.Tests;

/// <summary>A request the fake Maven upstream received.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path below the origin, without the leading slash (<c>org/acme/lib/1.0/lib-1.0.jar</c>).</param>
/// <param name="Authorization">The Authorization header.</param>
internal sealed record MavenUpstreamRequest(string Method, string Path, string? Authorization);

/// <summary>
/// An in-memory Maven repository standing in for Maven Central: files and their checksums (<c>.sha1</c>, <c>.md5</c>,
/// <c>.sha256</c>, <c>.sha512</c>, computed), artifact-level <c>maven-metadata.xml</c> (generated from the versions added, or
/// replaced), and knobs for what real repositories do to a proxy (credentials required, a root that lists nothing, an HTML page for
/// a missing file, a corrupt download, a repository without published checksums). Reached through
/// <see cref="ForwardingRemoteHttpClientFactory"/>.
/// </summary>
internal sealed class FakeMavenUpstream : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _metadata = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<MavenUpstreamRequest> _requests = new();

    /// <summary>The folder the repository lives in on the host (Maven Central is at <c>/maven2/</c>); requests outside it are 404.</summary>
    public string BasePath { get; set; } = "maven2/";

    /// <summary>When set, every request must carry exactly this Authorization header, else 401.</summary>
    public string? RequiredAuthorization { get; set; }

    /// <summary>The status the repository root answers (200 lists it; 404 is a repository that lists nothing).</summary>
    public HttpStatusCode RootStatus { get; set; } = HttpStatusCode.OK;

    /// <summary>Whether a missing path is answered 200 with an HTML page, as some servers' catch-all routes do.</summary>
    public bool HtmlForMissing { get; set; }

    /// <summary>Whether checksums are not published (every <c>.sha1</c> and the like is a 404).</summary>
    public bool NoChecksums { get; set; }

    /// <summary>Paths served with other bytes than their published checksums describe.</summary>
    public HashSet<string> Corrupt { get; } = new(StringComparer.Ordinal);

    /// <summary>The time the files claim to have been modified.</summary>
    public DateTimeOffset Modified { get; set; } = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Every request received.</summary>
    public MavenUpstreamRequest[] Requests => _requests.ToArray();

    /// <summary>How many requests whose path contains <paramref name="fragment"/> were received (any method).</summary>
    public int Count(string fragment) => _requests.Count(r => r.Path.Contains(fragment, StringComparison.Ordinal));

    /// <summary>How many GET requests whose path ends with <paramref name="suffix"/> were received.</summary>
    public int Gets(string suffix) => _requests.Count(r => r.Method == "GET" && r.Path.EndsWith(suffix, StringComparison.Ordinal));

    /// <summary>The upstream's folder of an artifact: <c>org/acme/lib</c>.</summary>
    public static string Folder(string group, string artifact) => MavenNames.GroupPath(group) + "/" + artifact;

    /// <summary>Adds a file of an artifact version under its Maven name; the artifact-level metadata lists the version.</summary>
    public byte[] AddFile(string group, string artifact, string version, string fileName, byte[] content)
    {
        _files[$"{Folder(group, artifact)}/{version}/{fileName}"] = content;

        return content;
    }

    /// <summary>Adds a release the usual way: a POM and a jar (their contents are returned).</summary>
    public (byte[] Pom, byte[] Jar) AddRelease(string group, string artifact, string version, string? jarText = null)
    {
        var pom = AddFile(group, artifact, version, $"{artifact}-{version}.pom", Encoding.UTF8.GetBytes(MavenPom.Generate(group, artifact, version, "jar", "From upstream " + artifact, "Upstream description")));
        var jar = AddFile(group, artifact, version, $"{artifact}-{version}.jar", Encoding.UTF8.GetBytes(jarText ?? $"jar of {group}:{artifact}:{version}"));

        return (pom, jar);
    }

    /// <summary>Replaces the artifact-level metadata the upstream serves (instead of generating it from the versions).</summary>
    public void SetMetadata(string path, string xml) => _metadata[path] = xml;

    /// <summary>Forgets a file (404 from then on).</summary>
    public void RemoveFile(string path) => _files.TryRemove(path, out _);

    /// <summary>The bytes of a file the upstream holds.</summary>
    public byte[] File(string path) => _files[path];

    /// <summary>The artifact-level metadata the upstream serves for <paramref name="folder"/>, or null when it has none.</summary>
    private string? MetadataOf(string folder)
    {
        if (_metadata.TryGetValue(folder + "/maven-metadata.xml", out var set)) return set;

        var versions = _files.Keys
            .Where(k => k.StartsWith(folder + "/", StringComparison.Ordinal))
            .Select(k => k.Substring(folder.Length + 1).Split('/'))
            .Where(parts => parts.Length == 2)
            .Select(parts => parts[0])
            .Distinct()
            .ToList();
        if (versions.Count == 0) return null;

        var slash = folder.LastIndexOf('/');
        var group = folder.Substring(0, slash).Replace('/', '.');

        return MavenMetadata.ForArtifact(group, folder.Substring(slash + 1), versions, Modified.UtcDateTime);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var full = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath).TrimStart('/');
        var inside = full.StartsWith(BasePath, StringComparison.Ordinal);
        var path = inside ? full.Substring(BasePath.Length) : full;
        _requests.Enqueue(new MavenUpstreamRequest(request.Method.Method, path, request.Headers.Authorization?.ToString()));
        if (!inside) return Task.FromResult(Plain(HttpStatusCode.NotFound, "outside the repository"));

        if (RequiredAuthorization != null && request.Headers.Authorization?.ToString() != RequiredAuthorization)
        {
            return Task.FromResult(Plain(HttpStatusCode.Unauthorized, "unauthorized"));
        }

        var head = request.Method == HttpMethod.Head;
        if (path.Length == 0) return Task.FromResult(RootStatus == HttpStatusCode.OK ? Plain(HttpStatusCode.OK, "<html>index</html>") : Plain(RootStatus, "no index"));

        var bytes = Resolve(path);
        if (bytes == null)
        {
            return Task.FromResult(HtmlForMissing
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html><body>Not here, but 200</body></html>", Encoding.UTF8, "text/html") }
                : Plain(HttpStatusCode.NotFound, "not found"));
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = head ? new ByteArrayContent(Array.Empty<byte>()) : new ByteArrayContent(bytes) };
        response.Content.Headers.ContentLength = bytes.Length;
        response.Content.Headers.LastModified = Modified;
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(path.EndsWith(".xml", StringComparison.Ordinal) ? "application/xml" : "application/octet-stream");

        return Task.FromResult(response);
    }

    private byte[]? Resolve(string path)
    {
        if (_files.TryGetValue(path, out var file))
        {
            return Corrupt.Contains(path) ? Encoding.UTF8.GetBytes("corrupt!") : file;
        }

        if (path.EndsWith("/maven-metadata.xml", StringComparison.Ordinal))
        {
            var xml = MetadataOf(path.Substring(0, path.Length - "/maven-metadata.xml".Length));

            return xml == null ? null : Encoding.UTF8.GetBytes(xml);
        }

        foreach (var algorithm in MavenNames.ChecksumAlgorithms)
        {
            var suffix = "." + algorithm;
            if (!path.EndsWith(suffix, StringComparison.Ordinal)) continue;

            if (NoChecksums) return null;

            var plain = path.Substring(0, path.Length - suffix.Length);
            // The checksum describes the real file, so a corrupt download is caught by it.
            var content = _files.TryGetValue(plain, out var real)
                ? real
                : plain.EndsWith("/maven-metadata.xml", StringComparison.Ordinal) && MetadataOf(plain.Substring(0, plain.Length - "/maven-metadata.xml".Length)) is { } xml
                    ? Encoding.UTF8.GetBytes(xml)
                    : null;
            if (content == null) return null;

            var hash = algorithm switch
            {
                "md5" => MD5.HashData(content),
                "sha256" => SHA256.HashData(content),
                "sha512" => SHA512.HashData(content),
                _ => SHA1.HashData(content),
            };

            return Encoding.ASCII.GetBytes(Convert.ToHexString(hash).ToLowerInvariant());
        }

        return null;
    }

    private static HttpResponseMessage Plain(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };
}
