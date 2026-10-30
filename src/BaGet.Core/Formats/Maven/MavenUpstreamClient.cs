using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace BaGet.Core;

/// <summary>How asking the upstream of a remote Maven repository for a small document (metadata, a checksum) ended.</summary>
public enum MavenUpstreamStatus
{
    /// <summary>The document is there (<see cref="MavenUpstreamAnswer.Content"/>).</summary>
    Found,

    /// <summary>The upstream does not have it (a real 404, possibly remembered).</summary>
    NotFound,

    /// <summary>Offline mode: the upstream is never contacted, and nothing is remembered about the path.</summary>
    Offline,

    /// <summary>The upstream could not be asked (down, refused the credentials, not a Maven repository) and nothing remembered can answer.</summary>
    Unavailable,
}

/// <summary>An upstream's answer about one small document.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Content">The bytes (a private copy the caller may keep), or null.</param>
/// <param name="Fetched">When the upstream sent them (UTC).</param>
/// <param name="Stale">Whether it is an expired answer served because the upstream could not be reached.</param>
/// <param name="Message">What failed, for <see cref="MavenUpstreamStatus.Unavailable"/>.</param>
public sealed record MavenUpstreamAnswer(MavenUpstreamStatus Status, byte[]? Content = null, DateTime Fetched = default, bool Stale = false, string? Message = null);

/// <summary>A file being downloaded from the upstream; disposing releases the connection.</summary>
public sealed class MavenUpstreamDownload : IDisposable
{
    private readonly HttpResponseMessage _response;

    internal MavenUpstreamDownload(HttpResponseMessage response, Stream content)
    {
        _response = response;
        Content = content;
    }

    /// <summary>The bytes.</summary>
    public Stream Content { get; }

    /// <summary>The size the upstream announced, or null.</summary>
    public long? Length => _response.Content.Headers.ContentLength;

    /// <summary>The media type the upstream announced, or null.</summary>
    public string? MediaType => _response.Content.Headers.ContentType?.MediaType;

    /// <summary>When the upstream says the file last changed (UTC), or null.</summary>
    public DateTime? LastModified => _response.Content.Headers.LastModified?.UtcDateTime;

    /// <inheritdoc/>
    public void Dispose()
    {
        Content.Dispose();
        _response.Dispose();
    }
}

/// <summary>
/// Asks the upstream of the current remote Maven repository (<see cref="IRepositoryContext"/>): the small documents
/// (<c>maven-metadata.xml</c> and checksums) and the bytes of a file. Applies what every remote repository has
/// (<see cref="RemoteRepositorySettings"/>): offline mode, the metadata period, the negative period (only a real 404 is remembered
/// as "not found"), an expired answer served when the upstream cannot be reached, the health the status page shows, credentials
/// sent to the upstream's own origin only (the HTTP client's handler) and <c>Remote:BlockPrivateAddresses</c> (its connect
/// callback). The answers are kept in <see cref="RemoteRepositoryState"/>, under keys that carry the request path after a
/// three-character kind (<c>mm:</c> metadata, <c>mc:</c> a checksum, <c>mf:</c> a file the upstream does not have), so a purge
/// finds everything about an artifact by the path prefix (<see cref="PathPrefix"/>).
/// </summary>
public sealed class MavenUpstreamClient
{
    /// <summary>The largest <c>maven-metadata.xml</c> accepted; the biggest real ones are well under a megabyte.</summary>
    public const long MaxMetadataBytes = 16L * 1024 * 1024;

    /// <summary>The largest checksum file accepted (a SHA-512 in hex is 128 characters).</summary>
    public const long MaxChecksumBytes = 4096;

    private readonly IRepositoryContext? _repository;
    private readonly IRemoteUpstreamSource _source;
    private readonly RemoteRepositoryState _state;
    private readonly IRemoteHttpClientFactory _http;
    private readonly ILogger<MavenUpstreamClient> _logger;

    public MavenUpstreamClient(
        IRepositoryContext? repository,
        IRemoteUpstreamSource source,
        RemoteRepositoryState state,
        IRemoteHttpClientFactory http,
        ILogger<MavenUpstreamClient> logger)
    {
        _repository = repository;
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The upstream of the request's repository, or null when it has none (not remote, or its settings are not usable).</summary>
    public Task<ResolvedUpstream?> ResolveAsync(CancellationToken cancellationToken)
        => _source.ResolveAsync(_repository?.Current ?? RepositoryInfo.Default, cancellationToken);

    private const string MetadataKind = "mm:";
    private const string ChecksumKind = "mc:";
    private const string AbsentKind = "mf:";

    /// <summary>
    /// Whether a cache key (any kind) is about the artifact folder <paramref name="relativeFolder"/> (<c>org/acme/lib</c>) or below it
    /// (a version folder, files, checksums, metadata).
    /// </summary>
    public static bool PathPrefix(string cacheKey, string relativeFolder)
        => cacheKey.Length > 3 && cacheKey.AsSpan(3).StartsWith((relativeFolder.TrimEnd('/') + "/").AsSpan(), StringComparison.Ordinal);

    /// <summary>
    /// The upstream's <c>maven-metadata.xml</c> at <paramref name="relativePath"/> (artifact, version or group level), validated as
    /// Maven metadata.
    /// </summary>
    public Task<MavenUpstreamAnswer> GetMetadataAsync(ResolvedUpstream upstream, string relativePath, CancellationToken cancellationToken)
        => GetDocumentAsync(upstream, MetadataKind, relativePath, MaxMetadataBytes, ValidateMetadata, cancellationToken);

    /// <summary>
    /// The upstream's checksum file at <paramref name="relativePath"/> (<c>….jar.sha1</c>), reduced to its hex digest.
    /// </summary>
    public Task<MavenUpstreamAnswer> GetChecksumAsync(ResolvedUpstream upstream, string relativePath, CancellationToken cancellationToken)
        => GetDocumentAsync(upstream, ChecksumKind, relativePath, MaxChecksumBytes, ValidateChecksum, cancellationToken);

    private async Task<MavenUpstreamAnswer> GetDocumentAsync(
        ResolvedUpstream upstream,
        string kind,
        string relativePath,
        long limit,
        Func<byte[], byte[]> validate,
        CancellationToken cancellationToken)
    {
        var key = upstream.Key;
        var settings = upstream.Settings;
        var cacheKey = kind + relativePath;
        var cached = _state.TryGet(key, cacheKey, out var value, out var negative, out var fresh);

        // Offline: whatever is remembered, however old; else nothing. The upstream is not contacted.
        if (settings.Offline)
        {
            if (cached && !negative && value is CachedDocument remembered)
            {
                _state.Count(key, RemoteRepositoryState.CacheEvent.Hit);

                return new MavenUpstreamAnswer(MavenUpstreamStatus.Found, (byte[])remembered.Content.Clone(), remembered.Fetched, Stale: !fresh);
            }

            return new MavenUpstreamAnswer(MavenUpstreamStatus.Offline);
        }

        if (cached && fresh)
        {
            if (negative)
            {
                _state.Count(key, RemoteRepositoryState.CacheEvent.NegativeHit);

                return new MavenUpstreamAnswer(MavenUpstreamStatus.NotFound);
            }

            if (value is CachedDocument hit)
            {
                _state.Count(key, RemoteRepositoryState.CacheEvent.Hit);

                return new MavenUpstreamAnswer(MavenUpstreamStatus.Found, (byte[])hit.Content.Clone(), hit.Fetched);
            }
        }

        _state.Count(key, RemoteRepositoryState.CacheEvent.Miss);

        try
        {
            using var response = await SendAsync(upstream, HttpMethod.Get, FileUri(settings, relativePath), "application/xml, text/plain, */*", cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _state.Succeeded(key);
                _state.Set(key, cacheKey, null, negative: true, TimeSpan.FromSeconds(settings.NegativeTtlSeconds));

                return new MavenUpstreamAnswer(MavenUpstreamStatus.NotFound);
            }

            if (!response.IsSuccessStatusCode) throw new HttpRequestException(null, null, response.StatusCode);

            byte[] content;
            await using (var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            {
                content = validate(await ReadCappedAsync(response, body, limit, cancellationToken).ConfigureAwait(false));
            }

            var fetched = _state.UtcNow;
            _state.Succeeded(key);
            _state.Set(key, cacheKey, new CachedDocument(content, fetched), negative: false, TimeSpan.FromSeconds(settings.MetadataTtlSeconds));

            return new MavenUpstreamAnswer(MavenUpstreamStatus.Found, (byte[])content.Clone(), fetched);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var what = Fail(upstream, e);

            if (cached && !negative && value is CachedDocument stale)
            {
                _state.Count(key, RemoteRepositoryState.CacheEvent.Stale);

                return new MavenUpstreamAnswer(MavenUpstreamStatus.Found, (byte[])stale.Content.Clone(), stale.Fetched, Stale: true);
            }

            return new MavenUpstreamAnswer(MavenUpstreamStatus.Unavailable, Message: what);
        }
    }

    /// <summary>
    /// Whether a download of <paramref name="relativePath"/> must not be tried now: a fresh 404 is remembered for it (counted as a
    /// negative hit). Offline is the caller's own check.
    /// </summary>
    public bool IsKnownAbsent(ResolvedUpstream upstream, string relativePath)
    {
        if (_state.TryGet(upstream.Key, AbsentKind + relativePath, out _, out var negative, out var fresh) && negative && fresh)
        {
            _state.Count(upstream.Key, RemoteRepositoryState.CacheEvent.NegativeHit);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Downloads the file at <paramref name="relativePath"/>; null when the upstream says 404 (remembered for the negative period).
    /// With <paramref name="headOnly"/> only the headers are asked (the download then has no content worth reading).
    /// </summary>
    /// <exception cref="MavenUpstreamException">The upstream could not be reached or refused.</exception>
    public async Task<MavenUpstreamDownload?> DownloadAsync(ResolvedUpstream upstream, string relativePath, bool headOnly, CancellationToken cancellationToken)
    {
        var key = upstream.Key;
        _state.Count(key, RemoteRepositoryState.CacheEvent.Miss);

        HttpResponseMessage? response = null;
        try
        {
            response = await SendAsync(upstream, headOnly ? HttpMethod.Head : HttpMethod.Get, FileUri(upstream.Settings, relativePath), "*/*", cancellationToken).ConfigureAwait(false);

            // A server whose catch-all route answers 200 with a web page for a path it does not have has no such file: an artifact is
            // never an HTML page, and keeping the page would put it in front of every build.
            if (response.StatusCode == HttpStatusCode.NotFound || IsWebPageForAFile(response, relativePath))
            {
                _state.Succeeded(key);
                _state.Set(key, AbsentKind + relativePath, null, negative: true, TimeSpan.FromSeconds(upstream.Settings.NegativeTtlSeconds));
                response.Dispose();

                return null;
            }

            if (!response.IsSuccessStatusCode) throw new HttpRequestException(null, null, response.StatusCode);

            var content = headOnly ? Stream.Null : await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            _state.Succeeded(key);

            return new MavenUpstreamDownload(response, content);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            response?.Dispose();

            throw new MavenUpstreamException(Fail(upstream, e));
        }
    }

    private static bool IsWebPageForAFile(HttpResponseMessage response, string relativePath)
        => response.IsSuccessStatusCode
           && string.Equals(response.Content.Headers.ContentType?.MediaType, "text/html", StringComparison.OrdinalIgnoreCase)
           && !relativePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
           && !relativePath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);

    /// <summary>Records that what the upstream sent was not what it announced (a file whose checksum differs): a failure of the upstream.</summary>
    public string Rejected(ResolvedUpstream upstream, string what)
    {
        _state.Failed(upstream.Key, what);
        _logger.LogWarning("The upstream of repository {Repository} sent something unusable: {Error}", upstream.Name, what);

        return what;
    }

    /// <summary>Records that the upstream answered (a successful probe of its own).</summary>
    internal void Reached(ResolvedUpstream upstream) => _state.Succeeded(upstream.Key);

    private static Uri FileUri(RemoteRepositorySettings settings, string relativePath)
    {
        var text = settings.UrlOrNull!.GetLeftPart(UriPartial.Path);

        return new Uri((text.EndsWith('/') ? text : text + "/") + relativePath);
    }

    private async Task<HttpResponseMessage> SendAsync(ResolvedUpstream upstream, HttpMethod method, Uri uri, string accept, CancellationToken cancellationToken)
    {
        var transport = _state.GetTransport(
            upstream.Key,
            upstream.Settings,
            () => new UpstreamTransport(upstream.Settings.TransportKey(), _http.Create(upstream.Settings), null));

        using var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("Accept", accept);

        return await transport.Http!.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
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

    // A server that answers 200 with an HTML page for a missing file is not a Maven repository's answer.
    private static byte[] ValidateMetadata(byte[] content)
    {
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(content), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var document = XDocument.Load(reader);
            if (document.Root?.Name.LocalName != "metadata") throw new InvalidDataException("The upstream did not send Maven metadata.");
        }
        catch (XmlException)
        {
            throw new InvalidDataException("The upstream did not send Maven metadata.");
        }

        return content;
    }

    // "<hex> [file name]" or "<hex>": the digest only.
    private static byte[] ValidateChecksum(byte[] content)
    {
        var text = Encoding.ASCII.GetString(content).Trim();
        var end = text.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
        var digest = (end < 0 ? text : text.Substring(0, end)).ToLowerInvariant();
        if (digest.Length < 32 || digest.Length > 128 || digest.Length % 2 != 0 || !IsHex(digest))
        {
            throw new InvalidDataException("The upstream did not send a checksum.");
        }

        return Encoding.ASCII.GetBytes(digest);
    }

    private static bool IsHex(string text)
    {
        foreach (var c in text)
        {
            if (!(c is >= '0' and <= '9' or >= 'a' and <= 'f')) return false;
        }

        return true;
    }

    private string Fail(ResolvedUpstream upstream, Exception e)
    {
        var what = RemoteUpstreamClient.Describe(e);
        _state.Failed(upstream.Key, what);

        // The exception itself is not logged: its message can carry the upstream's URL with a query string.
        _logger.LogWarning("The upstream of repository {Repository} could not be reached: {Error}", upstream.Name, what);

        return what;
    }

    private sealed record CachedDocument(byte[] Content, DateTime Fetched);
}
