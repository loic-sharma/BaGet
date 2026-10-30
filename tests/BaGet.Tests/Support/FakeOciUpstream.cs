using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Tests;

/// <summary>A request the fake registry received.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Host">The host asked (the registry, its token service or its CDN).</param>
/// <param name="Path">The path and query, without the leading slash.</param>
/// <param name="Authorization">The Authorization header.</param>
internal sealed record OciUpstreamRequest(string Method, string Host, string Path, string? Authorization);

/// <summary>
/// An in-memory Docker registry standing in for Docker Hub: manifests by tag and digest, blobs, tag lists (with <c>n</c>/<c>last</c>
/// and a <c>Link</c> header), the token flow of the real thing (a <c>401</c> with <c>Bearer realm</c> naming a separate token service
/// host, Basic credentials at the realm, per-image scopes, private images that need credentials), blobs redirected to a CDN host that
/// must not be given the registry's credentials, ranges, and knobs for what real registries do to a proxy (a corrupt layer, a
/// <c>Docker-Content-Digest</c> that lies, a catch-all error). Reached through <see cref="ForwardingRemoteHttpClientFactory"/>.
/// </summary>
internal sealed class FakeOciUpstream : HttpMessageHandler
{
    public const string RegistryHost = "registry.test";
    public const string AuthHost = "auth.test";
    public const string CdnHost = "cdn.test";
    public const string OtherRealmHost = "other-realm.test";
    public const string ManifestType = "application/vnd.oci.image.manifest.v1+json";

    private readonly ConcurrentDictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _tags = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, (string MediaType, byte[] Content)>> _manifests = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (string Scope, DateTime Expires)> _tokens = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<OciUpstreamRequest> _requests = new();
    private int _tokenCounter;

    /// <summary>Whether the registry asks for a token (the Docker Hub flow). Off, it is open.</summary>
    public bool TokenAuth { get; set; } = true;

    /// <summary>When set, the registry wants HTTP Basic with exactly this header value for everything, and sends a Basic challenge.</summary>
    public string? RequiredBasic { get; set; }

    /// <summary>The credentials the token service wants for private images (<c>user:password</c>); anonymous callers still get pull tokens for public ones.</summary>
    public string? TokenCredentials { get; set; }

    /// <summary>Images only a token obtained with <see cref="TokenCredentials"/> may pull.</summary>
    public HashSet<string> PrivateImages { get; } = new(StringComparer.Ordinal);

    /// <summary>Whether the token service refuses every credential (a wrong password).</summary>
    public bool TokenServiceRefuses { get; set; }

    /// <summary>Seconds a token lives, as <c>expires_in</c> says.</summary>
    public int TokenLifetimeSeconds { get; set; } = 300;

    /// <summary>Whether blob downloads are answered with a redirect to <see cref="CdnHost"/> (as Docker Hub does).</summary>
    public bool RedirectBlobs { get; set; }

    /// <summary>Whether the CDN answers <c>401</c> with a <c>Bearer</c> challenge naming a token service on <see cref="OtherRealmHost"/> (a host the registry did not choose).</summary>
    public bool CdnChallenges { get; set; }

    /// <summary>The requests that reached <see cref="OtherRealmHost"/>.</summary>
    public OciUpstreamRequest[] OtherRealmRequests => _requests.Where(r => r.Host == OtherRealmHost).ToArray();

    /// <summary>Blobs served with other bytes than their digest describes.</summary>
    public HashSet<string> Corrupt { get; } = new(StringComparer.Ordinal);

    /// <summary>Whether manifests announce a <c>Docker-Content-Digest</c> that is not theirs.</summary>
    public bool LyingDigest { get; set; }

    /// <summary>When set, every request to the registry is answered with this status (a registry that is up but unwell).</summary>
    public HttpStatusCode? FailWith { get; set; }

    /// <summary>Every request received.</summary>
    public OciUpstreamRequest[] Requests => _requests.ToArray();

    /// <summary>
    /// How many requests with this method whose path contains <paramref name="fragment"/> the registry host answered for real: those
    /// that carried a credential (a client's first request is the challenge round trip, which is not counted), or any when the registry is open.
    /// </summary>
    public int Count(string method, string fragment)
        => _requests.Count(r => r.Host == RegistryHost && r.Method == method && r.Path.Contains(fragment, StringComparison.Ordinal) && (r.Authorization != null || !TokenAuth));

    /// <summary>How many requests the token service received.</summary>
    public int TokenRequests => _requests.Count(r => r.Host == AuthHost);

    /// <summary>The requests the CDN received.</summary>
    public OciUpstreamRequest[] CdnRequests => _requests.Where(r => r.Host == CdnHost).ToArray();

    public static string Digest(byte[] content) => "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant();

    /// <summary>Adds a blob and returns its digest.</summary>
    public string AddBlob(byte[] content)
    {
        var digest = Digest(content);
        _blobs[digest] = content;

        return digest;
    }

    /// <summary>Adds a single-image manifest (config and layers become blobs) under <paramref name="tag"/> and by digest; returns its digest.</summary>
    public string AddImage(string image, string tag, string seed, int layers = 1, int layerSize = 96)
    {
        var config = Encoding.UTF8.GetBytes($"{{\"architecture\":\"amd64\",\"os\":\"linux\",\"seed\":\"{seed}\"}}");
        var configDigest = AddBlob(config);
        var descriptors = new List<string>();
        for (var i = 0; i < layers; i++)
        {
            var bytes = new byte[layerSize];
            new Random((seed + i).GetHashCode()).NextBytes(bytes);
            descriptors.Add($"{{\"mediaType\":\"application/vnd.oci.image.layer.v1.tar+gzip\",\"digest\":\"{AddBlob(bytes)}\",\"size\":{bytes.Length}}}");
        }

        var manifest = Encoding.UTF8.GetBytes(
            $"{{\"schemaVersion\":2,\"mediaType\":\"{ManifestType}\",\"config\":{{\"mediaType\":\"application/vnd.oci.image.config.v1+json\",\"digest\":\"{configDigest}\",\"size\":{config.Length}}},\"layers\":[{string.Join(",", descriptors)}]}}");

        return AddManifest(image, tag, ManifestType, manifest);
    }

    /// <summary>Adds a manifest document under a tag (when not null) and by digest; returns its digest.</summary>
    public string AddManifest(string image, string? tag, string mediaType, byte[] content)
    {
        var digest = Digest(content);
        var key = image;
        _manifests.GetOrAdd(key, _ => new(StringComparer.Ordinal))[digest] = (mediaType, content);
        if (tag != null) _tags.GetOrAdd(key, _ => new(StringComparer.Ordinal))[tag] = digest;

        return digest;
    }

    /// <summary>Adds a multi-platform index over manifests already added (<c>linux/amd64</c> and the like) under a tag; returns its digest.</summary>
    public string AddIndex(string image, string tag, params (string Manifest, string Os, string Architecture)[] children)
    {
        var entries = children.Select(c =>
            $"{{\"mediaType\":\"{ManifestType}\",\"digest\":\"{c.Manifest}\",\"size\":{_manifests[image][c.Manifest].Content.Length},\"platform\":{{\"architecture\":\"{c.Architecture}\",\"os\":\"{c.Os}\"}}}}");
        var content = Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"mediaType\":\"application/vnd.oci.image.index.v1+json\",\"manifests\":[" + string.Join(",", entries) + "]}");

        return AddManifest(image, tag, "application/vnd.oci.image.index.v1+json", content);
    }

    /// <summary>Points a tag at a manifest (a tag moving upstream).</summary>
    public void MoveTag(string image, string tag, string digest) => _tags.GetOrAdd(image, _ => new(StringComparer.Ordinal))[tag] = digest;

    /// <summary>The layer digests of an image added with <see cref="AddImage"/> (read from its manifest).</summary>
    public IReadOnlyList<string> LayersOf(string image, string tag)
    {
        var digest = _tags[image][tag];
        using var document = JsonDocument.Parse(_manifests[image][digest].Content);

        return document.RootElement.GetProperty("layers").EnumerateArray().Select(l => l.GetProperty("digest").GetString()!).ToList();
    }

    /// <summary>The config digest of an image added with <see cref="AddImage"/>.</summary>
    public string ConfigOf(string image, string tag)
    {
        var digest = _tags[image][tag];
        using var document = JsonDocument.Parse(_manifests[image][digest].Content);

        return document.RootElement.GetProperty("config").GetProperty("digest").GetString()!;
    }

    /// <summary>The bytes of a blob.</summary>
    public byte[] Blob(string digest) => _blobs[digest];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        _requests.Enqueue(new OciUpstreamRequest(request.Method.Method, uri.Host, uri.PathAndQuery.TrimStart('/'), request.Headers.Authorization?.ToString()));

        return Task.FromResult(uri.Host switch
        {
            AuthHost => Token(request),
            CdnHost => Cdn(request),
            OtherRealmHost => Status(HttpStatusCode.OK, "{\"token\":\"stolen\",\"expires_in\":300}"),
            _ => Registry(request),
        });
    }

    private static HttpResponseMessage Status(HttpStatusCode status, string? body = null, string? contentType = null)
    {
        var response = new HttpResponseMessage(status);
        if (body != null) response.Content = new StringContent(body, Encoding.UTF8, contentType ?? "application/json");

        return response;
    }

    // ---- the token service ----

    private HttpResponseMessage Token(HttpRequestMessage request)
    {
        if (TokenServiceRefuses) return Status(HttpStatusCode.Unauthorized, "{\"errors\":[{\"code\":\"UNAUTHORIZED\"}]}");

        var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
        var scope = query["scope"] ?? string.Empty;

        var credentialed = false;
        if (request.Headers.Authorization is { Scheme: "Basic" } basic)
        {
            var given = Encoding.UTF8.GetString(Convert.FromBase64String(basic.Parameter!));
            if (TokenCredentials == null || given != TokenCredentials) return Status(HttpStatusCode.Unauthorized, "{\"errors\":[{\"code\":\"UNAUTHORIZED\"}]}");

            credentialed = true;
        }

        // The grant: a private image only to credentials. The token is issued either way (Docker Hub does the same).
        var image = scope.StartsWith("repository:", StringComparison.Ordinal) ? scope.Split(':')[1] : string.Empty;
        var granted = image.Length == 0 || !PrivateImages.Contains(image) || credentialed ? scope : string.Empty;
        var token = "tok" + Interlocked.Increment(ref _tokenCounter);
        _tokens[token] = (granted, DateTime.UtcNow.AddSeconds(TokenLifetimeSeconds));

        return Status(HttpStatusCode.OK, $"{{\"token\":\"{token}\",\"access_token\":\"{token}\",\"expires_in\":{TokenLifetimeSeconds}}}");
    }

    // ---- the CDN ----

    private HttpResponseMessage Cdn(HttpRequestMessage request)
    {
        if (CdnChallenges)
        {
            var challenged = Status(HttpStatusCode.Unauthorized);
            challenged.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Bearer", $"realm=\"https://{OtherRealmHost}/token\",service=\"other\""));

            return challenged;
        }

        // A signed link: it must not be given the registry's credentials.
        if (request.Headers.Authorization != null) return Status(HttpStatusCode.BadRequest, "the CDN was sent a credential");

        var digest = request.RequestUri!.AbsolutePath.Split('/').Last();

        return Blob(request, digest, ranges: true);
    }

    // ---- the registry ----

    private HttpResponseMessage Registry(HttpRequestMessage request)
    {
        if (FailWith is { } failure) return Status(failure);

        var path = request.RequestUri!.AbsolutePath;
        if (!path.StartsWith("/v2/", StringComparison.Ordinal)) return Status(HttpStatusCode.NotFound);

        var rest = path.Substring("/v2/".Length);
        var name = string.IsNullOrEmpty(rest) ? null : ImageOf(rest);

        if (RequiredBasic != null && request.Headers.Authorization?.ToString() != RequiredBasic)
        {
            var response = Status(HttpStatusCode.Unauthorized);
            response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Basic", "realm=\"registry\""));

            return response;
        }

        if (TokenAuth && RequiredBasic == null)
        {
            var scope = name == null ? null : $"repository:{name}:pull";
            if (!TokenAccepted(request, scope))
            {
                var response = Status(HttpStatusCode.Unauthorized, "{\"errors\":[{\"code\":\"UNAUTHORIZED\"}]}");
                var challenge = $"realm=\"https://{AuthHost}/token\",service=\"registry.test\"" + (scope == null ? string.Empty : $",scope=\"{scope}\"");
                response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Bearer", challenge));

                return response;
            }
        }

        if (name == null) return Status(HttpStatusCode.OK, "{}");

        if (rest.EndsWith("/tags/list", StringComparison.Ordinal)) return TagList(request, name);

        var manifestsAt = rest.IndexOf("/manifests/", StringComparison.Ordinal);
        if (manifestsAt > 0) return Manifest(request, name, rest.Substring(manifestsAt + "/manifests/".Length));

        var blobsAt = rest.IndexOf("/blobs/", StringComparison.Ordinal);
        if (blobsAt > 0) return BlobRequest(request, name, Uri.UnescapeDataString(rest.Substring(blobsAt + "/blobs/".Length)));

        return Status(HttpStatusCode.NotFound);
    }

    // "library/nginx/manifests/latest" -> "library/nginx"
    private static string ImageOf(string rest)
    {
        foreach (var marker in new[] { "/manifests/", "/blobs/", "/tags/list" })
        {
            var at = rest.IndexOf(marker, StringComparison.Ordinal);
            if (at > 0) return rest.Substring(0, at);
        }

        return rest.TrimEnd('/');
    }

    private bool TokenAccepted(HttpRequestMessage request, string? scope)
    {
        if (request.Headers.Authorization is not { Scheme: "Bearer" } bearer || bearer.Parameter == null) return false;
        if (!_tokens.TryGetValue(bearer.Parameter, out var token) || token.Expires < DateTime.UtcNow) return false;

        // A token for the version check names no repository; one for an image must cover it (a private image's token, issued to
        // callers without credentials, covers nothing).
        return scope == null || token.Scope == scope;
    }

    private HttpResponseMessage Manifest(HttpRequestMessage request, string name, string reference)
    {

        var key = name;
        if (!_manifests.TryGetValue(key, out var byDigest)) return Status(HttpStatusCode.NotFound, "{\"errors\":[{\"code\":\"NAME_UNKNOWN\"}]}");

        var digest = reference.StartsWith("sha256:", StringComparison.Ordinal)
            ? reference
            : _tags.TryGetValue(key, out var tags) && tags.TryGetValue(reference, out var tagged) ? tagged : null;
        if (digest == null || !byDigest.TryGetValue(digest, out var manifest)) return Status(HttpStatusCode.NotFound, "{\"errors\":[{\"code\":\"MANIFEST_UNKNOWN\"}]}");

        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Docker-Content-Digest", LyingDigest ? "sha256:" + new string('0', 64) : digest);
        response.Content = request.Method == HttpMethod.Head ? new ByteArrayContent(Array.Empty<byte>()) : new ByteArrayContent(manifest.Content);
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(manifest.MediaType);
        if (request.Method == HttpMethod.Head) response.Content.Headers.ContentLength = manifest.Content.Length;

        return response;
    }

    private HttpResponseMessage TagList(HttpRequestMessage request, string name)
    {

        var key = name;
        if (!_tags.TryGetValue(key, out var tags)) return Status(HttpStatusCode.NotFound, "{\"errors\":[{\"code\":\"NAME_UNKNOWN\"}]}");

        var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
        var ordered = tags.Keys.OrderBy(t => t, StringComparer.Ordinal).Where(t => string.IsNullOrEmpty(query["last"]) || string.CompareOrdinal(t, query["last"]) > 0).ToList();

        // Pages of two, so a client has to follow the Link header.
        var page = ordered.Take(PageSize).ToList();
        var response = Status(HttpStatusCode.OK, JsonSerializer.Serialize(new { name, tags = page }));
        if (ordered.Count > page.Count)
        {
            response.Headers.TryAddWithoutValidation("Link", $"</v2/{name}/tags/list?last={Uri.EscapeDataString(page[^1])}>; rel=\"next\"");
        }

        return response;
    }

    /// <summary>Tags per page of the tag list.</summary>
    public int PageSize { get; set; } = 2;

    private HttpResponseMessage BlobRequest(HttpRequestMessage request, string name, string digest)
    {
        // A registry serves a blob under an image only when one of the image's manifests names it.
        if (!_blobs.ContainsKey(digest) || !_manifests.TryGetValue(name, out var held)
            || !held.Values.Any(m => Encoding.UTF8.GetString(m.Content).Contains(digest, StringComparison.Ordinal)))
        {
            return Status(HttpStatusCode.NotFound, "{\"errors\":[{\"code\":\"BLOB_UNKNOWN\"}]}");
        }

        if (RedirectBlobs)
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            redirect.Headers.Location = new Uri($"https://{CdnHost}/blobs/{digest}?sig=abc");

            return redirect;
        }

        return Blob(request, digest, ranges: true);
    }

    private HttpResponseMessage Blob(HttpRequestMessage request, string digest, bool ranges)
    {
        if (!_blobs.TryGetValue(digest, out var content)) return Status(HttpStatusCode.NotFound);
        if (Corrupt.Contains(digest)) content = content.Select(b => (byte)(b ^ 0xFF)).ToArray();

        var from = 0L;
        var to = content.Length - 1L;
        var partial = false;
        if (ranges && request.Headers.Range?.Ranges.FirstOrDefault() is { } range)
        {
            from = range.From ?? Math.Max(0, content.Length - (range.To ?? 0));
            to = range.From == null ? content.Length - 1L : Math.Min(range.To ?? content.Length - 1L, content.Length - 1L);
            partial = true;
        }

        var response = new HttpResponseMessage(partial ? HttpStatusCode.PartialContent : HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Docker-Content-Digest", digest);
        var slice = request.Method == HttpMethod.Head ? Array.Empty<byte>() : content.AsSpan((int)from, (int)(to - from + 1)).ToArray();
        response.Content = new ByteArrayContent(slice);
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        if (request.Method == HttpMethod.Head) response.Content.Headers.ContentLength = to - from + 1;
        if (partial) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, content.Length);

        return response;
    }
}
