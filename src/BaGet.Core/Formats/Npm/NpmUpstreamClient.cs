using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BaGet.Core;

/// <summary>How asking the upstream of a remote npm repository for a packument ended.</summary>
public enum NpmUpstreamStatus
{
    /// <summary>The document is there (<see cref="NpmUpstreamAnswer.Document"/>).</summary>
    Found,

    /// <summary>The upstream does not have the package (a real 404, possibly remembered).</summary>
    NotFound,

    /// <summary>Offline mode: the upstream is never contacted, and nothing is remembered about the package.</summary>
    Offline,

    /// <summary>The upstream could not be asked (down, refused the credentials, not a registry) and nothing remembered can answer.</summary>
    Unavailable,
}

/// <summary>An upstream's answer about one package.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Document">The packument (a private copy the caller may change), or null.</param>
/// <param name="Stale">Whether it is an expired answer served because the upstream could not be reached.</param>
/// <param name="Message">What failed, for <see cref="NpmUpstreamStatus.Unavailable"/>.</param>
public sealed record NpmUpstreamAnswer(NpmUpstreamStatus Status, JsonObject? Document = null, bool Stale = false, string? Message = null);

/// <summary>A tarball being downloaded from the upstream; disposing releases the connection.</summary>
public sealed class NpmUpstreamDownload : IDisposable
{
    private readonly HttpResponseMessage _response;

    internal NpmUpstreamDownload(HttpResponseMessage response, Stream content)
    {
        _response = response;
        Content = content;
    }

    /// <summary>The bytes.</summary>
    public Stream Content { get; }

    /// <summary>The size the upstream announced, or null.</summary>
    public long? Length => _response.Content.Headers.ContentLength;

    /// <inheritdoc/>
    public void Dispose()
    {
        Content.Dispose();
        _response.Dispose();
    }
}

/// <summary>
/// Asks the upstream registry of the current remote npm repository (<see cref="IRepositoryContext"/>): the packument of a
/// package and the bytes of a tarball. Applies what every remote repository has (<see cref="RemoteRepositorySettings"/>):
/// offline mode, the metadata period, the negative period (only a real 404 is remembered as "not found"), an expired answer
/// served when the upstream cannot be reached, the health the status page shows, credentials sent to the upstream's own origin
/// only (the HTTP client's handler) and <c>Remote:BlockPrivateAddresses</c> (its connect callback). The answers are kept in
/// <see cref="RemoteRepositoryState"/>, under keys that name the package with <c>/</c> written <c>%2f</c>
/// (<see cref="RemoteRepositoryState.ClearPackage"/> splits keys at the first <c>/</c>).
/// </summary>
public sealed class NpmUpstreamClient
{
    /// <summary>The largest packument accepted: some real ones are tens of megabytes; more is not a registry's answer.</summary>
    public const long MaxPackumentBytes = 128L * 1024 * 1024;

    /// <summary>The most packuments (and remembered 404s) kept per repository, far below NuGet's: a packument can be megabytes.</summary>
    public const int MaxCachedAnswers = 1000;

    private const string AbbreviatedType = "application/vnd.npm.install-v1+json";

    private readonly IRepositoryContext? _repository;
    private readonly IRemoteUpstreamSource _source;
    private readonly RemoteRepositoryState _state;
    private readonly IRemoteHttpClientFactory _http;
    private readonly ILogger<NpmUpstreamClient> _logger;

    public NpmUpstreamClient(
        IRepositoryContext? repository,
        IRemoteUpstreamSource source,
        RemoteRepositoryState state,
        IRemoteHttpClientFactory http,
        ILogger<NpmUpstreamClient> logger)
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

    /// <summary>The cache key part for a package: the name with the slash of a scope written <c>%2f</c>.</summary>
    internal static string KeyName(string name) => name.Replace("/", "%2f", StringComparison.Ordinal);

    private static string PackumentKey(string name, bool abbreviated) => (abbreviated ? "na:" : "nf:") + KeyName(name);

    private static string TarballKey(string name, string version) => "nd:" + KeyName(name) + "/" + version.ToLowerInvariant();

    /// <summary>The upstream's packument of <paramref name="name"/>, abbreviated (install-only) or full.</summary>
    public async Task<NpmUpstreamAnswer> GetPackumentAsync(ResolvedUpstream upstream, string name, bool abbreviated, CancellationToken cancellationToken)
    {
        var key = upstream.Key;
        var settings = upstream.Settings;
        var cacheKey = PackumentKey(name, abbreviated);
        var cached = _state.TryGet(key, cacheKey, out var value, out var negative, out var fresh);

        // The full document has everything the abbreviated one has: whichever is remembered can answer an install-only question.
        if (!cached && abbreviated)
        {
            cached = _state.TryGet(key, PackumentKey(name, abbreviated: false), out value, out negative, out fresh);
        }

        // Offline: whatever is remembered, however old; else nothing. The upstream is not contacted.
        if (settings.Offline)
        {
            if (cached && !negative && value is JsonObject remembered)
            {
                _state.Count(key, RemoteRepositoryState.CacheEvent.Hit);

                return new NpmUpstreamAnswer(NpmUpstreamStatus.Found, (JsonObject)remembered.DeepClone(), Stale: !fresh);
            }

            return new NpmUpstreamAnswer(NpmUpstreamStatus.Offline);
        }

        if (cached && fresh)
        {
            if (negative)
            {
                _state.Count(key, RemoteRepositoryState.CacheEvent.NegativeHit);

                return new NpmUpstreamAnswer(NpmUpstreamStatus.NotFound);
            }

            if (value is JsonObject hit)
            {
                _state.Count(key, RemoteRepositoryState.CacheEvent.Hit);

                return new NpmUpstreamAnswer(NpmUpstreamStatus.Found, (JsonObject)hit.DeepClone());
            }
        }

        _state.Count(key, RemoteRepositoryState.CacheEvent.Miss);

        try
        {
            using var response = await SendAsync(upstream, PackumentUri(settings, name), abbreviated ? AbbreviatedType + "; q=1.0, application/json; q=0.8, */*" : "application/json", cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _state.Succeeded(key);
                _state.Set(key, cacheKey, null, negative: true, TimeSpan.FromSeconds(settings.NegativeTtlSeconds), MaxCachedAnswers);

                return new NpmUpstreamAnswer(NpmUpstreamStatus.NotFound);
            }

            if (!response.IsSuccessStatusCode) throw new HttpRequestException(null, null, response.StatusCode);

            JsonObject document;
            await using (var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            {
                document = await ParseAsync(response, body, cancellationToken).ConfigureAwait(false);
            }

            _state.Succeeded(key);
            _state.Set(key, cacheKey, document, negative: false, TimeSpan.FromSeconds(settings.MetadataTtlSeconds), MaxCachedAnswers);

            return new NpmUpstreamAnswer(NpmUpstreamStatus.Found, (JsonObject)document.DeepClone());
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var what = Fail(upstream, e);

            if (cached && !negative && value is JsonObject stale)
            {
                _state.Count(key, RemoteRepositoryState.CacheEvent.Stale);

                return new NpmUpstreamAnswer(NpmUpstreamStatus.Found, (JsonObject)stale.DeepClone(), Stale: true);
            }

            return new NpmUpstreamAnswer(NpmUpstreamStatus.Unavailable, Message: what);
        }
    }

    /// <summary>
    /// Whether a download of <paramref name="version"/> must not be tried now: a fresh 404 is remembered for it (counted as a
    /// negative hit). Offline is the caller's own check.
    /// </summary>
    public bool IsKnownAbsent(ResolvedUpstream upstream, string name, string version)
    {
        if (_state.TryGet(upstream.Key, TarballKey(name, version), out _, out var negative, out var fresh) && negative && fresh)
        {
            _state.Count(upstream.Key, RemoteRepositoryState.CacheEvent.NegativeHit);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Downloads a tarball from <paramref name="url"/>; null when the upstream says 404 (remembered for the negative period).
    /// </summary>
    /// <exception cref="NpmUpstreamException">The upstream could not be reached or refused.</exception>
    public async Task<NpmUpstreamDownload?> DownloadAsync(ResolvedUpstream upstream, string name, string version, Uri url, CancellationToken cancellationToken)
    {
        var key = upstream.Key;
        _state.Count(key, RemoteRepositoryState.CacheEvent.Miss);

        HttpResponseMessage? response = null;
        try
        {
            response = await SendAsync(upstream, url, "application/octet-stream, */*", cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _state.Succeeded(key);
                _state.Set(key, TarballKey(name, version), null, negative: true, TimeSpan.FromSeconds(upstream.Settings.NegativeTtlSeconds), MaxCachedAnswers);
                response.Dispose();

                return null;
            }

            if (!response.IsSuccessStatusCode) throw new HttpRequestException(null, null, response.StatusCode);

            var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            _state.Succeeded(key);

            return new NpmUpstreamDownload(response, content);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            response?.Dispose();

            throw new NpmUpstreamException(Fail(upstream, e));
        }
    }

    /// <summary>Records that what the upstream sent was not what it announced (a tarball whose hash differs): a failure of the upstream.</summary>
    public string Rejected(ResolvedUpstream upstream, string what)
    {
        _state.Failed(upstream.Key, what);
        _logger.LogWarning("The upstream of repository {Repository} sent something unusable: {Error}", upstream.Name, what);

        return what;
    }

    /// <summary>The URL a version's tarball is fetched from: the packument's own when it is on the upstream's origin, else the conventional path.</summary>
    public static Uri TarballUri(RemoteRepositorySettings settings, string name, string version, string? announced)
    {
        var baseUri = settings.UrlOrNull!;
        if (Uri.TryCreate(announced, UriKind.Absolute, out var given)
            && string.Equals(given.GetLeftPart(UriPartial.Authority), baseUri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
        {
            // Same origin only: the packument is the upstream's word, and a tarball URL on another host would make this server
            // fetch whatever address a registry names (the HTTP client sends credentials to the typed origin only, but the
            // request itself would still go there).
            return given;
        }

        return new Uri(Base(settings) + EncodeName(name) + "/-/" + NpmNames.TarballFileName(name, version));
    }

    private static Uri PackumentUri(RemoteRepositorySettings settings, string name) => new(Base(settings) + EncodeName(name));

    private static string Base(RemoteRepositorySettings settings)
    {
        var text = settings.UrlOrNull!.GetLeftPart(UriPartial.Path);

        return text.EndsWith('/') ? text : text + "/";
    }

    private static string EncodeName(string name) => name.Replace("/", "%2f", StringComparison.Ordinal);

    private async Task<HttpResponseMessage> SendAsync(ResolvedUpstream upstream, Uri uri, string accept, CancellationToken cancellationToken)
    {
        var transport = _state.GetTransport(
            upstream.Key,
            upstream.Settings,
            () => new UpstreamTransport(upstream.Settings.TransportKey(), _http.Create(upstream.Settings), null));

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Accept", accept);

        return await transport.Http!.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonObject> ParseAsync(HttpResponseMessage response, Stream body, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaxPackumentBytes) throw new InvalidDataException("The packument is too large.");

        JsonNode? node;
        try
        {
            node = await JsonNode.ParseAsync(new CappedStream(body, MaxPackumentBytes), cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (System.Text.Json.JsonException)
        {
            throw new InvalidDataException("The upstream did not send JSON.");
        }

        return node as JsonObject ?? throw new InvalidDataException("The upstream did not send a packument.");
    }

    private string Fail(ResolvedUpstream upstream, Exception e)
    {
        var what = RemoteUpstreamClient.Describe(e);
        _state.Failed(upstream.Key, what);

        // The exception itself is not logged: its message can carry the upstream's URL with a query string.
        _logger.LogWarning("The upstream of repository {Repository} could not be reached: {Error}", upstream.Name, what);

        return what;
    }

    // Fails a read that goes past a limit, so a hostile upstream cannot fill the memory with one answer.
    private sealed class CappedStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _limit;
        private long _read;

        public CappedStream(Stream inner, long limit)
        {
            _inner = inner;
            _limit = limit;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Count(_inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Count(await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

        private int Count(int read)
        {
            _read += read;
            if (_read > _limit) throw new InvalidDataException("The answer is larger than " + _limit + " bytes.");

            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
