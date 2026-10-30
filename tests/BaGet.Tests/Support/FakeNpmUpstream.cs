using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Tests;

/// <summary>A request the fake npm upstream received.</summary>
/// <param name="Path">The unescaped path (a scoped name written <c>@scope/name</c>).</param>
/// <param name="RawPath">The path as sent (a scoped name keeps its <c>%2f</c>).</param>
/// <param name="Accept">The Accept header.</param>
/// <param name="Authorization">The Authorization header.</param>
internal sealed record NpmUpstreamRequest(string Path, string RawPath, string? Accept, string? Authorization);

/// <summary>
/// An in-memory npm registry standing in for registry.npmjs.org: packuments (full and install-only), tarballs, <c>/-/ping</c> and a
/// root document, with knobs for what real registries do to a proxy (credentials required, a missing ping, tarballs on another
/// host, a corrupt download, a slow tarball). Reached through <see cref="ForwardingRemoteHttpClientFactory"/>.
/// </summary>
internal sealed class FakeNpmUpstream : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, SortedDictionary<string, NpmTarballFile>> _packages = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _tags = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<NpmUpstreamRequest> _requests = new();
    private int _tarballDownloads;

    /// <summary>The authority the packuments' tarball URLs name by default.</summary>
    public string Origin { get; set; } = "http://registry.test/";

    /// <summary>When set, every request must carry exactly this Authorization header, else 401.</summary>
    public string? RequiredAuthorization { get; set; }

    /// <summary>Whether <c>/-/ping</c> is missing (404), as on registries that do not implement it.</summary>
    public bool PingMissing { get; set; }

    /// <summary>When set, tarball URLs in packuments name this origin instead of <see cref="Origin"/> (a CDN, or a trap).</summary>
    public string? TarballOrigin { get; set; }

    /// <summary>Tarballs (<c>name@version</c>) served with other bytes than the packument's integrity promises.</summary>
    public HashSet<string> Corrupt { get; } = new(StringComparer.Ordinal);

    /// <summary>Tarballs (<c>name@version</c>) the packument lists but the registry answers 404 for.</summary>
    public HashSet<string> MissingFiles { get; } = new(StringComparer.Ordinal);

    /// <summary>Every request received.</summary>
    public NpmUpstreamRequest[] Requests => _requests.ToArray();

    /// <summary>The tarball downloads served.</summary>
    public int TarballDownloads => _tarballDownloads;

    /// <summary>How many requests whose raw path contain <paramref name="fragment"/> were received.</summary>
    public int Count(string fragment) => _requests.Count(r => r.RawPath.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds versions of a package (a package is created by its first version). <c>latest</c> follows the highest.</summary>
    public NpmTarballFile[] Add(string name, params string[] versions)
    {
        var list = _packages.GetOrAdd(name, _ => new SortedDictionary<string, NpmTarballFile>(StringComparer.Ordinal));
        var added = new List<NpmTarballFile>();
        foreach (var version in versions)
        {
            var tarball = NpmTestPackages.Tarball(name, version, description: "From upstream " + name);
            lock (list) list[version] = tarball;
            added.Add(tarball);
        }

        var tags = _tags.GetOrAdd(name, _ => new Dictionary<string, string>(StringComparer.Ordinal));
        lock (tags)
        {
            tags["latest"] = BaGet.Core.NpmNames.Highest(list.Keys)!;
        }

        return added.ToArray();
    }

    /// <summary>Forgets a package: the registry then answers 404 for it.</summary>
    public void Remove(string name)
    {
        _packages.TryRemove(name, out _);
        _tags.TryRemove(name, out _);
    }

    /// <summary>Points a dist-tag at a version.</summary>
    public void Tag(string name, string tag, string version)
    {
        var tags = _tags.GetOrAdd(name, _ => new Dictionary<string, string>(StringComparer.Ordinal));
        lock (tags) tags[tag] = version;
    }

    /// <summary>The tarball of a version the registry holds.</summary>
    public NpmTarballFile Tarball(string name, string version) => _packages[name][version];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var raw = uri.AbsolutePath;
        var path = Uri.UnescapeDataString(raw);
        _requests.Enqueue(new NpmUpstreamRequest(path, raw, request.Headers.Accept.ToString(), request.Headers.Authorization?.ToString()));

        if (RequiredAuthorization != null && request.Headers.Authorization?.ToString() != RequiredAuthorization)
        {
            return Task.FromResult(Plain(HttpStatusCode.Unauthorized, "{\"error\":\"unauthorized\"}"));
        }

        if (path == "/-/ping") return Task.FromResult(PingMissing ? Plain(HttpStatusCode.NotFound, "{}") : Plain(HttpStatusCode.OK, "{}"));
        if (path == "/") return Task.FromResult(Plain(HttpStatusCode.OK, "{\"db_name\":\"registry\"}"));

        var trimmed = path.TrimStart('/');
        var marker = trimmed.IndexOf("/-/", StringComparison.Ordinal);
        if (marker >= 0)
        {
            return Task.FromResult(ServeTarball(trimmed.Substring(0, marker), trimmed.Substring(marker + 3)));
        }

        return Task.FromResult(Packument(trimmed, abbreviated: request.Headers.Accept.ToString().Contains("application/vnd.npm.install-v1+json", StringComparison.Ordinal)));
    }

    private HttpResponseMessage Packument(string name, bool abbreviated)
    {
        if (!_packages.TryGetValue(name, out var versions)) return Plain(HttpStatusCode.NotFound, "{\"error\":\"Not found\"}");

        var origin = (TarballOrigin ?? Origin).TrimEnd('/') + "/";
        var versionsNode = new JsonObject();
        var time = new JsonObject { ["created"] = "2019-01-01T00:00:00.000Z" };
        var day = 1;
        lock (versions)
        {
            foreach (var pair in versions)
            {
                var unscoped = name.Contains('/') ? name.Substring(name.IndexOf('/') + 1) : name;
                versionsNode[pair.Key] = new JsonObject
                {
                    ["name"] = name,
                    ["version"] = pair.Key,
                    ["description"] = "From upstream " + name,
                    ["dist"] = new JsonObject
                    {
                        ["tarball"] = $"{origin}{name}/-/{unscoped}-{pair.Key}.tgz",
                        ["integrity"] = pair.Value.Integrity,
                        ["shasum"] = pair.Value.Shasum,
                    },
                };
                time[pair.Key] = $"2019-02-{day++:00}T10:00:00.000Z";
            }
        }

        time["modified"] = "2019-03-01T00:00:00.000Z";
        var tags = new JsonObject();
        lock (_tags[name])
        {
            foreach (var tag in _tags[name]) tags[tag.Key] = tag.Value;
        }

        var document = abbreviated
            ? new JsonObject { ["name"] = name, ["modified"] = "2019-03-01T00:00:00.000Z", ["dist-tags"] = tags, ["versions"] = versionsNode }
            : new JsonObject
            {
                ["_id"] = name,
                ["_rev"] = "9-upstream",
                ["name"] = name,
                ["description"] = "From upstream " + name,
                ["readme"] = "# " + name,
                ["dist-tags"] = tags,
                ["versions"] = versionsNode,
                ["time"] = time,
            };

        var response = Plain(HttpStatusCode.OK, document.ToJsonString());
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(abbreviated ? "application/vnd.npm.install-v1+json" : "application/json");

        return response;
    }

    private HttpResponseMessage ServeTarball(string name, string file)
    {
        if (!_packages.TryGetValue(name, out var versions)) return Plain(HttpStatusCode.NotFound, "{}");

        var unscoped = name.Contains('/') ? name.Substring(name.IndexOf('/') + 1) : name;
        if (!BaGet.Core.NpmNames.TryParseTarballFile(name, file, out var version) || !versions.TryGetValue(version, out var tarball) || file != $"{unscoped}-{version}.tgz")
        {
            return Plain(HttpStatusCode.NotFound, "{}");
        }

        if (MissingFiles.Contains($"{name}@{version}")) return Plain(HttpStatusCode.NotFound, "{}");

        Interlocked.Increment(ref _tarballDownloads);
        var bytes = Corrupt.Contains($"{name}@{version}") ? tarball.Bytes.Reverse().ToArray() : tarball.Bytes;
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        return response;
    }

    private static HttpResponseMessage Plain(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
