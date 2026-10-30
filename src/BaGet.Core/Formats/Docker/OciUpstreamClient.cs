using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BaGet.Core;

/// <summary>How asking the upstream of a remote Docker repository ended.</summary>
public enum OciUpstreamStatus
{
    /// <summary>The upstream answered.</summary>
    Found,

    /// <summary>The upstream does not have it (a real 404, possibly remembered).</summary>
    NotFound,

    /// <summary>Offline mode: the upstream is never contacted.</summary>
    Offline,

    /// <summary>The upstream could not be asked (down, refused the credentials, not a registry) and nothing remembered can answer.</summary>
    Unavailable,
}

/// <summary>An upstream's answer about a manifest.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Digest">The manifest's digest (computed over the bytes).</param>
/// <param name="MediaType">Its media type.</param>
/// <param name="Content">The bytes (a private copy the caller may keep).</param>
/// <param name="Stale">Whether it is an expired answer served because the upstream could not be reached.</param>
/// <param name="Message">What failed, for <see cref="OciUpstreamStatus.Unavailable"/>.</param>
public sealed record OciManifestAnswer(OciUpstreamStatus Status, string? Digest = null, string? MediaType = null, byte[]? Content = null, bool Stale = false, string? Message = null);

/// <summary>An upstream's answer about a blob's size.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Size">The size in bytes.</param>
/// <param name="Message">What failed, for <see cref="OciUpstreamStatus.Unavailable"/>.</param>
public sealed record OciBlobHeadAnswer(OciUpstreamStatus Status, long Size = 0, string? Message = null);

/// <summary>An upstream's tag list.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Tags">The tags (every page the upstream offered, up to a limit).</param>
/// <param name="Stale">Whether it is an expired list served because the upstream could not be reached.</param>
/// <param name="Message">What failed, for <see cref="OciUpstreamStatus.Unavailable"/>.</param>
public sealed record OciTagsAnswer(OciUpstreamStatus Status, IReadOnlyList<string>? Tags = null, bool Stale = false, string? Message = null);

/// <summary>A blob (or a range of one) being downloaded from the upstream; disposing releases the connection.</summary>
public sealed class OciUpstreamBlob : IDisposable
{
    private readonly HttpResponseMessage _response;

    internal OciUpstreamBlob(HttpResponseMessage response, Stream content, long totalSize, long offset, long length)
    {
        _response = response;
        Content = content;
        TotalSize = totalSize;
        Offset = offset;
        Length = length;
    }

    /// <summary>The bytes.</summary>
    public Stream Content { get; }

    /// <summary>The size of the whole blob.</summary>
    public long TotalSize { get; }

    /// <summary>Where the bytes start.</summary>
    public long Offset { get; }

    /// <summary>How many bytes follow.</summary>
    public long Length { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        Content.Dispose();
        _response.Dispose();
    }
}

/// <summary>The upstream of a remote Docker repository could not be reached or sent something unusable (the registry answers 502).</summary>
public sealed class OciUpstreamException : Exception
{
    /// <param name="message">What failed, safe to show (no URL, header or credential).</param>
    public OciUpstreamException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Asks the upstream registry of the current remote Docker repository (<see cref="IRepositoryContext"/>) for manifests, blobs and
/// tags, and does the authentication a registry asks for: the first request goes without credentials; a <c>401</c> with
/// <c>WWW-Authenticate: Bearer realm="…",service="…",scope="…"</c> sends the client to that realm for a token (HTTP Basic with the
/// configured credentials, or anonymously: Docker Hub hands anonymous pull tokens out), which is remembered per scope until it
/// expires and then sent as the bearer credential; a Basic challenge is answered with the credentials at once, to the registry's own
/// origin. Applies what every remote repository has (<see cref="RemoteRepositorySettings"/>): offline mode, the metadata period,
/// the negative period (only a real 404 is remembered as "not found"), an expired answer served when the upstream cannot be
/// reached, the health the status page shows, and <c>Remote:BlockPrivateAddresses</c> (the HTTP client's connect callback; the
/// token service and a redirect go through the same client).
/// </summary>
/// <remarks>
/// <para>Credentials go to the registry's origin and to the realm its challenge names, never to a redirect target on another origin
/// (Docker Hub redirects blobs to a CDN; the CDN is given nothing). A realm on plain http under an https registry is refused (no
/// downgrade). Redirects are followed by hand, at most <see cref="MaxRedirects"/>.</para>
/// <para>Answers are kept in <see cref="RemoteRepositoryState"/> under keys that carry the upstream name after a three-character
/// kind (<c>om:</c> manifest, <c>ob:</c> blob size, <c>ol:</c> tag list), so a purge finds everything about an image by prefix
/// (<see cref="ImagePrefix"/>).</para>
/// </remarks>
public sealed class OciUpstreamClient
{
    /// <summary>The most redirects followed for one request.</summary>
    public const int MaxRedirects = 5;

    /// <summary>The most pages of a tag list read from the upstream.</summary>
    public const int MaxTagPages = 20;

    /// <summary>The most tags kept from one upstream list.</summary>
    public const int MaxTags = 100000;

    private const int MaxTokenBytes = 1024 * 1024;
    private const int MaxTagPageBytes = 16 * 1024 * 1024;
    private const int ManifestCacheLimit = 2000;

    private const string ManifestKind = "om:";
    private const string BlobKind = "ob:";
    private const string TagsKind = "ol:";

    // Docker Hub's "official images" live under library/: `docker pull nginx` asks for library/nginx.
    private static readonly string[] DockerHubHosts = { "registry-1.docker.io", "index.docker.io", "registry.hub.docker.com", "docker.io" };

    private static readonly string AcceptManifests = string.Join(
        ", ",
        OciNames.IndexTypes.Concat(OciNames.ImageManifestTypes));

    private readonly IRepositoryContext? _repository;
    private readonly IRemoteUpstreamSource _source;
    private readonly RemoteRepositoryState _state;
    private readonly IRemoteHttpClientFactory _http;
    private readonly ILogger<OciUpstreamClient> _logger;
    private readonly Microsoft.Extensions.Options.IOptionsSnapshot<OciOptions>? _options;

    public OciUpstreamClient(
        IRepositoryContext? repository,
        IRemoteUpstreamSource source,
        RemoteRepositoryState state,
        IRemoteHttpClientFactory http,
        ILogger<OciUpstreamClient> logger,
        Microsoft.Extensions.Options.IOptionsSnapshot<OciOptions>? options = null)
    {
        _repository = repository;
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options;
    }

    private int MaxManifestBytes => _options?.Value.MaxManifestBytes ?? new OciOptions().MaxManifestBytes;

    /// <summary>The upstream of the request's repository, or null when it has none (not remote, or its settings are not usable).</summary>
    public Task<ResolvedUpstream?> ResolveAsync(CancellationToken cancellationToken)
        => _source.ResolveAsync(_repository?.Current ?? RepositoryInfo.Default, cancellationToken);

    /// <summary>
    /// Whether a cache key (any kind) is about <paramref name="image"/> (a purge drops these).
    /// </summary>
    public static bool ImagePrefix(string cacheKey, string image)
        => cacheKey.Length > 3 && cacheKey.AsSpan(3).StartsWith((image + "|").AsSpan(), StringComparison.Ordinal);

    /// <summary>
    /// The name the upstream knows an image by: the image itself, except at Docker Hub where a single-component name is an official
    /// image and lives under <c>library/</c>.
    /// </summary>
    public static string UpstreamName(RemoteRepositorySettings settings, string image)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        var host = settings.UrlOrNull?.IdnHost;
        if (host != null && !image.Contains('/') && DockerHubHosts.Contains(host, StringComparer.OrdinalIgnoreCase)) return "library/" + image;

        return image;
    }

    // The key of an image's answers: "<kind><image>|<detail>" (images never contain '|').
    private static string Key(string kind, string image, string detail) => kind + image + "|" + detail;

    // ---- manifests -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The manifest <paramref name="reference"/> (a tag or a digest) of <paramref name="image"/>, validated (supported type, a
    /// document that parses, the digest it claims and, for a digest reference, the digest asked for).
    /// </summary>
    /// <param name="upstream">The upstream.</param>
    /// <param name="image">The image name as clients use it here.</param>
    /// <param name="reference">A tag or a <c>sha256:</c> digest.</param>
    /// <param name="known">
    /// The manifest this repository already stores for the reference, if any: when the cached answer has expired the upstream is first
    /// asked with <c>HEAD</c> (which Docker Hub does not count as a pull) and, when it names the same digest, <paramref name="known"/>
    /// is the answer and the document is not downloaded again.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    public async Task<OciManifestAnswer> GetManifestAsync(ResolvedUpstream upstream, string image, string reference, OciManifestContent? known, CancellationToken cancellationToken)
    {
        var key = upstream.Key;
        var settings = upstream.Settings;
        var cacheKey = Key(ManifestKind, image, reference);
        var cached = _state.TryGet(key, cacheKey, out var value, out var negative, out var fresh);

        if (settings.Offline)
        {
            if (cached && !negative && value is CachedManifest remembered)
            {
                _state.Count(key, RemoteRepositoryState.CacheEvent.Hit);

                return remembered.ToAnswer(stale: !fresh);
            }

            return new OciManifestAnswer(OciUpstreamStatus.Offline);
        }

        if (cached && fresh)
        {
            if (negative)
            {
                _state.Count(key, RemoteRepositoryState.CacheEvent.NegativeHit);

                return new OciManifestAnswer(OciUpstreamStatus.NotFound);
            }

            if (value is CachedManifest hit)
            {
                _state.Count(key, RemoteRepositoryState.CacheEvent.Hit);

                return hit.ToAnswer(stale: false);
            }
        }

        _state.Count(key, RemoteRepositoryState.CacheEvent.Miss);
        var name = UpstreamName(settings, image);
        var ttl = TimeSpan.FromSeconds(settings.MetadataTtlSeconds);

        try
        {
            // A tag the upstream still points at the manifest we hold needs no download.
            var heldDigest = (value as CachedManifest)?.Digest ?? known?.Digest;
            if (heldDigest != null && !OciNames.LooksLikeDigest(reference))
            {
                using var head = await SendAsync(upstream, HttpMethod.Head, ManifestUri(settings, name, reference), AcceptManifests, name, null, cancellationToken).ConfigureAwait(false);
                if (head.StatusCode == HttpStatusCode.OK
                    && head.Headers.TryGetValues(DigestHeader, out var digests)
                    && string.Equals(digests.FirstOrDefault(), heldDigest, StringComparison.Ordinal))
                {
                    var current = (value as CachedManifest)
                        ?? (known == null ? null : new CachedManifest(known.Digest, known.MediaType, known.Content));
                    if (current != null)
                    {
                        _state.Succeeded(key);
                        _state.Set(key, cacheKey, current, negative: false, ttl, ManifestCacheLimit);

                        return current.ToAnswer(stale: false);
                    }
                }
            }

            using var response = await SendAsync(upstream, HttpMethod.Get, ManifestUri(settings, name, reference), AcceptManifests, name, null, cancellationToken).ConfigureAwait(false);

            if (IsAbsent(response.StatusCode))
            {
                _state.Succeeded(key);
                _state.Set(key, cacheKey, null, negative: true, TimeSpan.FromSeconds(settings.NegativeTtlSeconds), ManifestCacheLimit);

                return new OciManifestAnswer(OciUpstreamStatus.NotFound);
            }

            if (!response.IsSuccessStatusCode) throw new HttpRequestException(null, null, response.StatusCode);

            byte[] bytes;
            await using (var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            {
                bytes = await ReadCappedAsync(response, body, MaxManifestBytes, cancellationToken).ConfigureAwait(false);
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            var digest = OciNames.DigestOf(StreamingStorage.Sha256Hex(bytes));
            if (OciNames.LooksLikeDigest(reference) && !string.Equals(reference, digest, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The manifest the upstream sent does not have the digest asked for.");
            }

            if (response.Headers.TryGetValues(DigestHeader, out var claimed) && claimed.FirstOrDefault() is { } claim && !string.Equals(claim, digest, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The manifest the upstream sent does not have the digest it announced.");
            }

            mediaType = ManifestMediaType(mediaType, bytes);
            try
            {
                OciManifestParser.Parse(mediaType, bytes);
            }
            catch (OciException e)
            {
                throw new InvalidDataException("The upstream sent a manifest this registry cannot use: " + e.Message);
            }

            var answer = new CachedManifest(digest, mediaType!, bytes);
            _state.Succeeded(key);
            _state.Set(key, cacheKey, answer, negative: false, ttl, ManifestCacheLimit);

            return answer.ToAnswer(stale: false);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var what = Fail(upstream, e);

            if (cached && !negative && value is CachedManifest stale)
            {
                _state.Count(key, RemoteRepositoryState.CacheEvent.Stale);

                return stale.ToAnswer(stale: true);
            }

            return new OciManifestAnswer(OciUpstreamStatus.Unavailable, Message: what);
        }
    }

    // A registry names the type in Content-Type; one that sends application/json (or nothing) is read from the document's own mediaType.
    private static string? ManifestMediaType(string? header, byte[] content)
    {
        if (OciNames.IsSupportedManifestType(header)) return header;

        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("mediaType", out var declared)
                && declared.ValueKind == JsonValueKind.String
                && OciNames.IsSupportedManifestType(declared.GetString()))
            {
                return declared.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return header;
    }

    private const string DigestHeader = "Docker-Content-Digest";

    /// <summary>
    /// Whether the upstream has the manifest (a <c>HEAD</c>; nothing is downloaded, kept or counted as a pull): the resolution preview.
    /// </summary>
    public async Task<OciUpstreamStatus> ProbeManifestAsync(ResolvedUpstream upstream, string image, string reference, CancellationToken cancellationToken)
    {
        if (upstream.Settings.Offline) return OciUpstreamStatus.Offline;

        var name = UpstreamName(upstream.Settings, image);
        try
        {
            using var response = await SendAsync(upstream, HttpMethod.Head, ManifestUri(upstream.Settings, name, reference), AcceptManifests, name, null, cancellationToken).ConfigureAwait(false);
            if (IsAbsent(response.StatusCode)) return OciUpstreamStatus.NotFound;
            if (!response.IsSuccessStatusCode) throw new HttpRequestException(null, null, response.StatusCode);

            _state.Succeeded(upstream.Key);

            return OciUpstreamStatus.Found;
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Fail(upstream, e);

            return OciUpstreamStatus.Unavailable;
        }
    }

    // ---- blobs -----------------------------------------------------------------------------------------------------------

    /// <summary>The size of a blob the upstream holds for <paramref name="image"/> (a <c>HEAD</c>), remembered for the metadata period.</summary>
    public async Task<OciBlobHeadAnswer> HeadBlobAsync(ResolvedUpstream upstream, string image, string digest, CancellationToken cancellationToken)
    {
        var key = upstream.Key;
        var settings = upstream.Settings;
        var cacheKey = Key(BlobKind, image, digest);
        var cached = _state.TryGet(key, cacheKey, out var value, out var negative, out var fresh);

        if (settings.Offline)
        {
            return cached && !negative && value is long size
                ? new OciBlobHeadAnswer(OciUpstreamStatus.Found, size)
                : new OciBlobHeadAnswer(OciUpstreamStatus.Offline);
        }

        if (cached && fresh)
        {
            if (negative)
            {
                _state.Count(key, RemoteRepositoryState.CacheEvent.NegativeHit);

                return new OciBlobHeadAnswer(OciUpstreamStatus.NotFound);
            }

            if (value is long hit)
            {
                _state.Count(key, RemoteRepositoryState.CacheEvent.Hit);

                return new OciBlobHeadAnswer(OciUpstreamStatus.Found, hit);
            }
        }

        _state.Count(key, RemoteRepositoryState.CacheEvent.Miss);
        var name = UpstreamName(settings, image);

        try
        {
            using var response = await SendAsync(upstream, HttpMethod.Head, BlobUri(settings, name, digest), "*/*", name, null, cancellationToken).ConfigureAwait(false);

            if (IsAbsent(response.StatusCode))
            {
                _state.Succeeded(key);
                _state.Set(key, cacheKey, null, negative: true, TimeSpan.FromSeconds(settings.NegativeTtlSeconds), ManifestCacheLimit);

                return new OciBlobHeadAnswer(OciUpstreamStatus.NotFound);
            }

            if (!response.IsSuccessStatusCode) throw new HttpRequestException(null, null, response.StatusCode);

            var length = response.Content.Headers.ContentLength ?? throw new InvalidDataException("The upstream did not announce the size of the blob.");
            _state.Succeeded(key);
            _state.Set(key, cacheKey, length, negative: false, TimeSpan.FromSeconds(settings.MetadataTtlSeconds), ManifestCacheLimit);

            return new OciBlobHeadAnswer(OciUpstreamStatus.Found, length);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var what = Fail(upstream, e);

            return cached && !negative && value is long stale
                ? new OciBlobHeadAnswer(OciUpstreamStatus.Found, stale)
                : new OciBlobHeadAnswer(OciUpstreamStatus.Unavailable, Message: what);
        }
    }

    /// <summary>
    /// Downloads a blob (or, with <paramref name="rangeStart"/>, the bytes from there to <paramref name="rangeEnd"/> inclusive, or the
    /// end); null when the upstream says 404 (remembered for the negative period).
    /// </summary>
    /// <exception cref="OciUpstreamException">The upstream could not be reached or refused.</exception>
    public async Task<OciUpstreamBlob?> OpenBlobAsync(ResolvedUpstream upstream, string image, string digest, long? rangeStart, long? rangeEnd, CancellationToken cancellationToken)
    {
        var key = upstream.Key;
        var settings = upstream.Settings;
        _state.Count(key, RemoteRepositoryState.CacheEvent.Miss);
        var name = UpstreamName(settings, image);

        HttpResponseMessage? response = null;
        try
        {
            string? range = rangeStart == null ? null : $"bytes={rangeStart.Value}-{(rangeEnd == null ? string.Empty : rangeEnd.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))}";
            response = await SendAsync(upstream, HttpMethod.Get, BlobUri(settings, name, digest), "*/*", name, range, cancellationToken).ConfigureAwait(false);

            if (IsAbsent(response.StatusCode))
            {
                _state.Succeeded(key);
                _state.Set(key, Key(BlobKind, image, digest), null, negative: true, TimeSpan.FromSeconds(settings.NegativeTtlSeconds), ManifestCacheLimit);
                response.Dispose();

                return null;
            }

            if (!response.IsSuccessStatusCode) throw new HttpRequestException(null, null, response.StatusCode);

            long total;
            long offset = 0;
            long length;
            if (response.StatusCode == HttpStatusCode.PartialContent && response.Content.Headers.ContentRange is { } contentRange)
            {
                offset = contentRange.From ?? 0;
                length = response.Content.Headers.ContentLength ?? ((contentRange.To ?? 0) - offset + 1);
                total = contentRange.Length ?? throw new InvalidDataException("The upstream did not announce the size of the blob.");
            }
            else
            {
                // A server that ignores Range sends the whole blob; the caller skips what it does not want.
                total = length = response.Content.Headers.ContentLength ?? throw new InvalidDataException("The upstream did not announce the size of the blob.");
            }

            var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            _state.Succeeded(key);

            return new OciUpstreamBlob(response, content, total, offset, length);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            response?.Dispose();

            throw new OciUpstreamException(Fail(upstream, e));
        }
    }

    // ---- tags ------------------------------------------------------------------------------------------------------------

    /// <summary>The tags of <paramref name="image"/> at the upstream (all pages, up to a limit), remembered for the metadata period.</summary>
    public async Task<OciTagsAnswer> ListTagsAsync(ResolvedUpstream upstream, string image, CancellationToken cancellationToken)
    {
        var key = upstream.Key;
        var settings = upstream.Settings;
        var cacheKey = Key(TagsKind, image, "all");
        var cached = _state.TryGet(key, cacheKey, out var value, out var negative, out var fresh);

        if (settings.Offline)
        {
            return cached && !negative && value is IReadOnlyList<string> remembered
                ? new OciTagsAnswer(OciUpstreamStatus.Found, remembered, Stale: !fresh)
                : new OciTagsAnswer(OciUpstreamStatus.Offline);
        }

        if (cached && fresh)
        {
            if (negative)
            {
                _state.Count(key, RemoteRepositoryState.CacheEvent.NegativeHit);

                return new OciTagsAnswer(OciUpstreamStatus.NotFound);
            }

            if (value is IReadOnlyList<string> hit)
            {
                _state.Count(key, RemoteRepositoryState.CacheEvent.Hit);

                return new OciTagsAnswer(OciUpstreamStatus.Found, hit);
            }
        }

        _state.Count(key, RemoteRepositoryState.CacheEvent.Miss);
        var name = UpstreamName(settings, image);

        try
        {
            var tags = new List<string>();
            var next = new Uri(RegistryBase(settings) + "v2/" + name + "/tags/list");
            for (var page = 0; next != null && page < MaxTagPages; page++)
            {
                using var response = await SendAsync(upstream, HttpMethod.Get, next, "application/json", name, null, cancellationToken).ConfigureAwait(false);

                if (IsAbsent(response.StatusCode))
                {
                    if (page > 0) break;

                    _state.Succeeded(key);
                    _state.Set(key, cacheKey, null, negative: true, TimeSpan.FromSeconds(settings.NegativeTtlSeconds), ManifestCacheLimit);

                    return new OciTagsAnswer(OciUpstreamStatus.NotFound);
                }

                if (!response.IsSuccessStatusCode) throw new HttpRequestException(null, null, response.StatusCode);

                byte[] bytes;
                await using (var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                {
                    bytes = await ReadCappedAsync(response, body, MaxTagPageBytes, cancellationToken).ConfigureAwait(false);
                }

                try
                {
                    using var document = JsonDocument.Parse(bytes);
                    if (document.RootElement.TryGetProperty("tags", out var list) && list.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tag in list.EnumerateArray())
                        {
                            if (tag.ValueKind == JsonValueKind.String && OciNames.IsValidTag(tag.GetString()) && tags.Count < MaxTags) tags.Add(tag.GetString()!);
                        }
                    }
                    else if (!document.RootElement.TryGetProperty("tags", out _))
                    {
                        throw new InvalidDataException("The upstream did not send a tag list.");
                    }
                }
                catch (JsonException)
                {
                    throw new InvalidDataException("The upstream did not send a tag list.");
                }

                next = NextLink(response, new Uri(RegistryBase(settings)));
            }

            var ordered = (IReadOnlyList<string>)tags.Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToList();
            _state.Succeeded(key);
            _state.Set(key, cacheKey, ordered, negative: false, TimeSpan.FromSeconds(settings.MetadataTtlSeconds), ManifestCacheLimit);

            return new OciTagsAnswer(OciUpstreamStatus.Found, ordered);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var what = Fail(upstream, e);

            return cached && !negative && value is IReadOnlyList<string> stale
                ? new OciTagsAnswer(OciUpstreamStatus.Found, stale, Stale: true)
                : new OciTagsAnswer(OciUpstreamStatus.Unavailable, Message: what);
        }
    }

    // The next page of a list: the Link header's rel="next" target, on the registry's own origin only.
    private static Uri? NextLink(HttpResponseMessage response, Uri origin)
    {
        if (!response.Headers.TryGetValues("Link", out var links)) return null;

        foreach (var link in links.SelectMany(l => l.Split(',')))
        {
            if (!link.Contains("rel=\"next\"", StringComparison.OrdinalIgnoreCase) && !link.Contains("rel=next", StringComparison.OrdinalIgnoreCase)) continue;

            var start = link.IndexOf('<');
            var end = link.IndexOf('>');
            if (start < 0 || end < start) continue;

            if (Uri.TryCreate(origin, link.Substring(start + 1, end - start - 1), out var next) && SameOrigin(origin, next)) return next;
        }

        return null;
    }

    // ---- probing ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Whether the upstream answers the version check (<c>GET /v2/</c>) once authenticated: the check Test connection runs. Throws
    /// what a failed request throws.
    /// </summary>
    /// <returns>The status of the final answer.</returns>
    public async Task<HttpStatusCode> PingAsync(ResolvedUpstream upstream, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(upstream, HttpMethod.Get, new Uri(RegistryBase(upstream.Settings) + "v2/"), "application/json", repository: null, range: null, cancellationToken).ConfigureAwait(false);

        return response.StatusCode;
    }

    /// <summary>Records that what the upstream sent was not what it announced.</summary>
    public string Rejected(ResolvedUpstream upstream, string what)
    {
        _state.Failed(upstream.Key, what);
        _logger.LogWarning("The upstream of repository {Repository} sent something unusable: {Error}", upstream.Name, what);

        return what;
    }

    /// <summary>Records that the upstream answered.</summary>
    internal void Reached(ResolvedUpstream upstream) => _state.Succeeded(upstream.Key);

    /// <summary>
    /// Whether a remembered answer is about <paramref name="image"/> (or, with a reference, only that manifest, the tag lists and the
    /// answers about blobs would not change): what a purge drops.
    /// </summary>
    public static bool MatchesImage(string cacheKey, string image, string? reference)
        => ImagePrefix(cacheKey, image)
           && (reference == null || cacheKey.StartsWith(TagsKind, StringComparison.Ordinal) || cacheKey == Key(ManifestKind, image, reference));

    /// <summary>Drops the remembered answers about <paramref name="image"/> (or, with a reference, only that manifest's and the tag list's).</summary>
    public int Forget(ResolvedUpstream upstream, string image, string? reference = null)
        => _state.ClearMatching(upstream.Key, key => MatchesImage(key, image, reference));

    // ---- plumbing --------------------------------------------------------------------------------------------------------

    /// <summary>The registry's address with a trailing slash and without a trailing <c>/v2</c> (both ways of writing it work).</summary>
    public static string RegistryBase(RemoteRepositorySettings settings)
    {
        var text = settings.UrlOrNull!.GetLeftPart(UriPartial.Path).TrimEnd('/');
        if (text.EndsWith("/v2", StringComparison.OrdinalIgnoreCase)) text = text.Substring(0, text.Length - 3);

        return text + "/";
    }

    private static Uri ManifestUri(RemoteRepositorySettings settings, string name, string reference)
        => new(RegistryBase(settings) + "v2/" + name + "/manifests/" + reference);

    private static Uri BlobUri(RemoteRepositorySettings settings, string name, string digest)
        => new(RegistryBase(settings) + "v2/" + name + "/blobs/" + digest);

    private static bool SameOrigin(Uri a, Uri b)
        => string.Equals(a.GetLeftPart(UriPartial.Authority), b.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);

    private UpstreamTransport TransportOf(ResolvedUpstream upstream)
        => _state.GetTransport(
            upstream.Key,
            upstream.Settings,
            () => new UpstreamTransport(upstream.Settings.TransportKey(), _http.CreateProbe(upstream.Settings), null));

    /// <summary>
    /// Sends one request with the authentication the upstream asks for and follows redirects by hand (the credential stays on the
    /// registry's origin). The response is returned unread.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        ResolvedUpstream upstream,
        HttpMethod method,
        Uri uri,
        string accept,
        string? repository,
        string? range,
        CancellationToken cancellationToken)
    {
        var settings = upstream.Settings;
        var transport = TransportOf(upstream);
        var origin = settings.UrlOrNull!;
        var scopeKey = repository == null ? "*" : "repository:" + repository + ":pull";

        AuthenticationHeaderValue? authorization = null;
        if (transport.Tokens.TryGetValue(scopeKey, out var token) && token.ExpiresUtc > _state.UtcNow)
        {
            authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        }
        else if (settings.HasCredentials && string.IsNullOrEmpty(settings.Username) && !string.IsNullOrEmpty(settings.Password))
        {
            // A password with no user name is a ready-made bearer token for the registry itself.
            authorization = new AuthenticationHeaderValue("Bearer", settings.Password);
        }
        else if (transport.BasicChallenged && settings.HasCredentials)
        {
            authorization = BasicHeader(settings);
        }

        for (var attempt = 0; ; attempt++)
        {
            var (response, answeredBy) = await SendFollowingRedirectsAsync(transport, method, uri, accept, range, authorization, origin, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.Unauthorized || attempt >= 1) return response;

            // A challenge is answered only when it came from the registry's own origin. A 401 from where a redirect led (a CDN, an
            // object store) names a realm that host chose: the credentials must not go there. The caller sees a plain 401 (an absence).
            if (!SameOrigin(answeredBy, origin)) return response;

            var challenges = response.Headers.WwwAuthenticate;
            var bearer = challenges.FirstOrDefault(c => string.Equals(c.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase));
            var basic = challenges.FirstOrDefault(c => string.Equals(c.Scheme, "Basic", StringComparison.OrdinalIgnoreCase));

            if (bearer != null && !string.IsNullOrEmpty(bearer.Parameter))
            {
                var parameters = ParseChallenge(bearer.Parameter);
                response.Dispose();

                var scope = parameters.GetValueOrDefault("scope") ?? (repository == null ? null : "repository:" + repository + ":pull");
                var fetched = await FetchTokenAsync(transport, settings, parameters, scope, cancellationToken).ConfigureAwait(false);
                transport.Tokens[scopeKey] = fetched;
                authorization = new AuthenticationHeaderValue("Bearer", fetched.Value);

                continue;
            }

            if (basic != null && settings.HasCredentials && !string.IsNullOrEmpty(settings.Username))
            {
                response.Dispose();
                transport.BasicChallenged = true;
                authorization = BasicHeader(settings);

                continue;
            }

            return response;
        }
    }

    // The response and the address that answered it (the last hop of the redirects).
    private async Task<(HttpResponseMessage Response, Uri AnsweredBy)> SendFollowingRedirectsAsync(
        UpstreamTransport transport,
        HttpMethod method,
        Uri uri,
        string accept,
        string? range,
        AuthenticationHeaderValue? authorization,
        Uri origin,
        CancellationToken cancellationToken)
    {
        var current = uri;
        for (var hops = 0; ; hops++)
        {
            using var request = new HttpRequestMessage(method, current);
            request.Headers.TryAddWithoutValidation("Accept", accept);
            if (range != null) request.Headers.TryAddWithoutValidation("Range", range);

            // The credential goes to the registry's own origin only: a redirect (Docker Hub sends blobs to a CDN) is given none.
            if (authorization != null && SameOrigin(current, origin)) request.Headers.Authorization = authorization;

            var response = await transport.Http!.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!IsRedirect(response.StatusCode) || response.Headers.Location == null) return (response, current);

            var next = new Uri(current, response.Headers.Location);
            response.Dispose();
            if (hops >= MaxRedirects) throw new InvalidDataException("The upstream redirected too many times.");
            if (next.Scheme != Uri.UriSchemeHttp && next.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("The upstream redirected to an address that is not http or https.");
            if (current.Scheme == Uri.UriSchemeHttps && next.Scheme == Uri.UriSchemeHttp) throw new InvalidDataException("The upstream redirected from https to http.");

            current = next;
        }
    }

    // The upstream has no such thing for this caller: a 404, or a 401/403 even after the token service gave a token (Docker Hub answers
    // an image that does not exist, or that is private, like that). A refusal of the credentials themselves is the token service's.
    private static bool IsAbsent(HttpStatusCode status) => status is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    private static bool IsRedirect(HttpStatusCode status)
        => status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static AuthenticationHeaderValue BasicHeader(RemoteRepositorySettings settings)
        => new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes((settings.Username ?? string.Empty) + ":" + (settings.Password ?? string.Empty))));

    // `Bearer realm="https://auth.docker.io/token",service="registry.docker.io",scope="repository:library/nginx:pull"`
    internal static Dictionary<string, string> ParseChallenge(string parameter)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        while (i < parameter.Length)
        {
            while (i < parameter.Length && (parameter[i] == ' ' || parameter[i] == ',')) i++;
            var equals = parameter.IndexOf('=', i);
            if (equals < 0) break;

            var name = parameter.Substring(i, equals - i).Trim();
            i = equals + 1;
            string value;
            if (i < parameter.Length && parameter[i] == '"')
            {
                i++;
                var builder = new StringBuilder();
                while (i < parameter.Length && parameter[i] != '"')
                {
                    if (parameter[i] == '\\' && i + 1 < parameter.Length) i++;
                    builder.Append(parameter[i]);
                    i++;
                }

                i++;
                value = builder.ToString();
            }
            else
            {
                var end = parameter.IndexOf(',', i);
                if (end < 0) end = parameter.Length;
                value = parameter.Substring(i, end - i).Trim();
                i = end;
            }

            if (name.Length > 0) result[name] = value;
        }

        return result;
    }

    // Asks the realm named in the challenge for a token: the credentials as HTTP Basic when configured, else anonymously.
    private async Task<UpstreamToken> FetchTokenAsync(
        UpstreamTransport transport,
        RemoteRepositorySettings settings,
        Dictionary<string, string> challenge,
        string? scope,
        CancellationToken cancellationToken)
    {
        if (!challenge.TryGetValue("realm", out var realmText) || !Uri.TryCreate(realmText, UriKind.Absolute, out var realm)
            || (realm.Scheme != Uri.UriSchemeHttp && realm.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidDataException("The upstream's authentication challenge names no usable token service.");
        }

        if (settings.UrlOrNull!.Scheme == Uri.UriSchemeHttps && realm.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("The upstream's token service is not on https although the registry is.");
        }

        var query = new StringBuilder();
        void Add(string name, string? value)
        {
            if (string.IsNullOrEmpty(value)) return;

            query.Append(query.Length == 0 ? string.Empty : "&").Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value));
        }

        Add("service", challenge.GetValueOrDefault("service"));
        Add("scope", scope);
        if (!string.IsNullOrEmpty(settings.Username)) Add("account", settings.Username);

        var builder = new UriBuilder(realm);
        builder.Query = string.IsNullOrEmpty(realm.Query) ? query.ToString() : realm.Query.TrimStart('?') + (query.Length == 0 ? string.Empty : "&" + query);

        // The realm may be another host than the registry (Docker Hub: auth.docker.io); only the credentials of this repository go there.
        using var request = new HttpRequestMessage(HttpMethod.Get, builder.Uri);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        if (settings.HasCredentials && !string.IsNullOrEmpty(settings.Username)) request.Headers.Authorization = BasicHeader(settings);

        using var response = await transport.Http!.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new InvalidDataException(settings.HasCredentials
                ? "The upstream's token service refused the credentials."
                : "The upstream's token service wants credentials.");
        }

        if (!response.IsSuccessStatusCode) throw new HttpRequestException(null, null, response.StatusCode);

        byte[] bytes;
        await using (var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            bytes = await ReadCappedAsync(response, body, MaxTokenBytes, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            string? value = null;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("token", out var token) && token.ValueKind == JsonValueKind.String) value = token.GetString();
                if (string.IsNullOrEmpty(value) && root.TryGetProperty("access_token", out var access) && access.ValueKind == JsonValueKind.String) value = access.GetString();
            }

            if (string.IsNullOrEmpty(value)) throw new InvalidDataException("The upstream's token service sent no token.");

            var seconds = 60;
            if (root.TryGetProperty("expires_in", out var expires) && expires.ValueKind == JsonValueKind.Number && expires.TryGetInt32(out var parsed)) seconds = parsed;

            // Renewed a little early; a token that claims to live under a few seconds is used for one request at least.
            var life = TimeSpan.FromSeconds(Math.Clamp(seconds - 10, 5, 3600));

            return new UpstreamToken(value!, _state.UtcNow + life);
        }
        catch (JsonException)
        {
            throw new InvalidDataException("The upstream's token service did not send a token.");
        }
    }

    private static async Task<byte[]> ReadCappedAsync(HttpResponseMessage response, Stream body, long limit, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("The answer is too large.");

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit) throw new InvalidDataException("The answer is larger than " + limit + " bytes.");

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private string Fail(ResolvedUpstream upstream, Exception e)
    {
        // A refusal this client raised itself already says what it is; anything else is described by type, never by message
        // (an HTTP exception's message can carry the URL).
        var what = e is InvalidDataException ? e.Message : RemoteUpstreamClient.Describe(e);
        if (e is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } http)
        {
            what = $"The upstream refused access (HTTP {(int)http.StatusCode}); check the credentials and that the image exists.";
        }

        _state.Failed(upstream.Key, what);

        // The exception itself is not logged: its message can carry the upstream's URL with a query string.
        _logger.LogWarning("The upstream of repository {Repository} could not be used: {Error}", upstream.Name, what);

        return what;
    }

    private sealed record CachedManifest(string Digest, string MediaType, byte[] Content)
    {
        public OciManifestAnswer ToAnswer(bool stale) => new(OciUpstreamStatus.Found, Digest, MediaType, (byte[])Content.Clone(), stale);
    }
}
