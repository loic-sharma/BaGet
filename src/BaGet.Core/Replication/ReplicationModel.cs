using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>
/// One replicable item, named the way the format's retention hooks name things: a NuGet package version, an npm version, a Maven
/// file (<see cref="Part"/>) or a Docker tag.
/// </summary>
/// <param name="Name">Package id, npm name, Maven <c>group:artifact</c>, image name.</param>
/// <param name="Version">Version; a Docker tag.</param>
/// <param name="Part">The Maven file name; empty for the other formats.</param>
/// <param name="Size">Bytes, when the listing knows them (0 otherwise).</param>
/// <param name="Digest">A content digest that is equal on two servers holding the same content (npm <c>integrity</c>, Maven sha256, Docker manifest digest); empty when the format keeps none (NuGet).</param>
public sealed record ReplicaItem(string Name, string Version, string Part, long Size, string Digest)
{
    /// <summary>Whether two sides hold different content: both digests are known and differ.</summary>
    public bool DiffersFrom(ReplicaItem other)
        => other != null && Digest.Length > 0 && other.Digest.Length > 0 && !string.Equals(Digest, other.Digest, StringComparison.Ordinal);

    /// <summary>The item as the ledger and the logs name it.</summary>
    public override string ToString() => Part.Length == 0 ? Name + " " + Version : Name + " " + Version + " " + Part;
}

/// <summary>What <c>GET /api/v1/replication/inventory/{repository}</c> answers: the repository and its items.</summary>
/// <param name="Repository">The repository's name.</param>
/// <param name="Format">Its format.</param>
/// <param name="Kind">Local, remote or virtual (only a local repository can receive).</param>
/// <param name="Count">The number of items.</param>
/// <param name="Items">The items (empty when only the summary was asked for).</param>
public sealed record ReplicationInventory(string Repository, RepositoryFormat Format, RepositoryKind Kind, int Count, IReadOnlyList<ReplicaItem> Items);

/// <summary>An item could not be replicated; the run logs the message and goes on with the next item.</summary>
public sealed class ReplicationItemException : Exception
{
    public ReplicationItemException(string message, bool conflict = false, Exception? inner = null)
        : base(message, inner)
    {
        Conflict = conflict;
    }

    /// <summary>The target refused because it holds the item already (a version cannot be published twice).</summary>
    public bool Conflict { get; }
}

/// <summary>
/// What a format needs to replicate: list what the local repository holds, copy an item to or from the peer through the format's
/// own protocol, and delete one on either side. Registered (scoped) beside the format's module and used inside the local repository's
/// scope (<see cref="IRepositoryContext"/>); the peer is another BaGet server (<see cref="ReplicationPeer"/>). Local writes are the
/// administrator's job, not a user's: no caller is checked, but the maintenance gate is.
/// </summary>
public interface IFormatReplicator : IFormatService
{
    /// <summary>How names are compared (NuGet ids ignore case).</summary>
    StringComparer NameComparer => StringComparer.Ordinal;

    /// <summary>Everything the local repository holds that replication copies. A virtual repository holds none.</summary>
    Task<IReadOnlyList<ReplicaItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Sends the local item to the peer's repository.</summary>
    /// <returns>Bytes of content sent.</returns>
    /// <exception cref="ReplicationItemException">The item could not be sent.</exception>
    Task<long> PushAsync(ReplicaItem item, ReplicationPeer peer, CancellationToken cancellationToken);

    /// <summary>Fetches the item from the peer's repository and stores it locally.</summary>
    /// <returns>Bytes of content received.</returns>
    /// <exception cref="ReplicationItemException">The item could not be fetched or stored.</exception>
    Task<long> PullAsync(ReplicaItem item, ReplicationPeer peer, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the local copy of the item with the peer's (<c>SourceWins</c> on a pull). A format that cannot overwrite in place must
    /// fetch and judge the peer's item (integrity, scanning policy) <b>before</b> it removes the local copy, so a refused or failed
    /// replacement leaves the local copy as it was. The default (delete, then <see cref="PullAsync"/>) is only for a format that cannot do so.
    /// </summary>
    /// <returns>Bytes of content received.</returns>
    /// <exception cref="ReplicationItemException">The item could not be fetched or stored.</exception>
    async Task<long> PullReplacingAsync(ReplicaItem item, ReplicationPeer peer, CancellationToken cancellationToken)
    {
        await DeleteLocalAsync(item, cancellationToken).ConfigureAwait(false);

        return await PullAsync(item, peer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether <see cref="PushAsync"/> replaces the peer's copy of an item in place (<c>SourceWins</c> on a push). A Docker tag does:
    /// the manifest <c>PUT</c> moves it. NuGet, npm and Maven peers refuse to overwrite a stored version or release file, so the engine
    /// deletes the peer's copy first (default <see langword="false"/>), and a peer that then refuses the item stays without it until the next run.
    /// </summary>
    bool PushReplacesInPlace => false;

    /// <summary>Deletes the item from the local repository for good.</summary>
    Task DeleteLocalAsync(ReplicaItem item, CancellationToken cancellationToken);

    /// <summary>Deletes the item from the peer's repository through the format's protocol.</summary>
    Task DeletePeerAsync(ReplicaItem item, ReplicationPeer peer, CancellationToken cancellationToken);
}

/// <summary>
/// Another BaGet server's repository, reached with an HTTP client that already carries the credential for that server's origin
/// (and no other). The replicators build their protocol URLs from it. The credential is the peer's API key, sent as a bearer token.
/// </summary>
public sealed class ReplicationPeer : IDisposable
{
    private static readonly JsonSerializerOptions Json = CreateJson();

    private readonly bool _ownsClient;

    /// <param name="http">A client that authenticates to <paramref name="server"/>.</param>
    /// <param name="server">The server's base address.</param>
    /// <param name="repository">The peer's repository name.</param>
    /// <param name="ownsClient">Whether <see cref="Dispose"/> disposes <paramref name="http"/>.</param>
    /// <param name="apiKey">The key the client sends, for the protocols that read it from their own header (NuGet's <c>X-NuGet-ApiKey</c>); never logged.</param>
    public ReplicationPeer(HttpClient http, Uri server, string repository, bool ownsClient = true, string? apiKey = null)
    {
        ApiKey = apiKey;
        Http = http ?? throw new ArgumentNullException(nameof(http));
        if (server == null) throw new ArgumentNullException(nameof(server));
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _ownsClient = ownsClient;
        var text = server.AbsoluteUri;
        Server = text.EndsWith('/') ? server : new Uri(text + "/");
    }

    public HttpClient Http { get; }

    /// <summary>The peer's API key, for the one protocol (NuGet push) that wants it in a header of its own.</summary>
    public string? ApiKey { get; }

    /// <summary>The server's base address, ending with a slash.</summary>
    public Uri Server { get; }

    /// <summary>The header that carries the peer's key for the protocols that read it from their own header (NuGet).</summary>
    internal const string ApiKeyHeader = "X-NuGet-ApiKey";

    /// <summary>
    /// Adds the peer's key to <paramref name="request"/> (a NuGet request, which reads it from its own header) in <see cref="ApiKeyHeader"/>, only when the request goes to the peer's own origin;
    /// any other origin gets no credential and an existing one is removed.
    /// </summary>
    public void AddCredentials(HttpRequestMessage request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        request.Headers.Remove(ApiKeyHeader);
        if (ApiKey != null && request.RequestUri != null && SameOrigin(request.RequestUri)) request.Headers.TryAddWithoutValidation(ApiKeyHeader, ApiKey);
    }

    private bool SameOrigin(Uri uri) => uri.IsAbsoluteUri
        && string.Equals(uri.Scheme, Server.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.Host, Server.Host, StringComparison.OrdinalIgnoreCase)
        && uri.Port == Server.Port;

    /// <summary>The redirects a GET or HEAD follows by hand.</summary>
    internal const int MaxRedirects = 5;

    /// <summary>
    /// Sends <paramref name="request"/> to the peer. The client does not follow redirects (the factory builds it that way), so a
    /// credential can never be carried to a redirect target: a GET or HEAD redirect is followed here, getting the credential header
    /// only when the target is the peer's own origin; any other redirect, or a redirect of a request with a body, is returned as is
    /// (and fails as a non-success answer).
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completion, CancellationToken cancellationToken)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        // Only a request that asked for the key header (the NuGet protocol's) gets it; the others authenticate in the client's handler.
        var withKey = request.Headers.Contains(ApiKeyHeader);
        if (withKey) AddCredentials(request);
        var response = await Http.SendAsync(request, completion, cancellationToken).ConfigureAwait(false);
        for (var hops = 0; hops < MaxRedirects; hops++)
        {
            var location = response.Headers.Location;
            var redirect = (int)response.StatusCode is 301 or 302 or 303 or 307 or 308;
            if (!redirect || location == null || request.Content != null || (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)) break;

            var target = request.RequestUri != null && !location.IsAbsoluteUri ? new Uri(request.RequestUri, location) : location;
            if (!target.IsAbsoluteUri || (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps)) break;
            // Never from https to http.
            if (request.RequestUri != null && request.RequestUri.Scheme == Uri.UriSchemeHttps && target.Scheme == Uri.UriSchemeHttp) break;

            response.Dispose();
            var next = new HttpRequestMessage(request.Method, target);
            foreach (var header in request.Headers)
            {
                if (!string.Equals(header.Key, ApiKeyHeader, StringComparison.OrdinalIgnoreCase) && !string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase)) next.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (withKey) AddCredentials(next);
            request = next;
            response = await Http.SendAsync(request, completion, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    /// <summary>The peer's repository.</summary>
    public string Repository { get; }

    /// <summary>The path in front of the peer repository's routes: nothing for <c>default</c>, else <c>r/{name}/</c>.</summary>
    public string PathPrefix => string.Equals(Repository, BaGet.Core.Repository.DefaultName, StringComparison.Ordinal)
        ? string.Empty
        : "r/" + Uri.EscapeDataString(Repository) + "/";

    /// <summary>A URL below the peer repository's feed root (<c>{server}/r/{name}/{relative}</c>).</summary>
    public Uri Feed(string relative) => new(Server, PathPrefix + relative);

    /// <summary>A URL below the server root (<c>{server}/{relative}</c>), where Docker's <c>v2</c> routes live.</summary>
    public Uri Root(string relative) => new(Server, relative);

    /// <summary>Asks the peer what its repository holds (<c>summary</c>: only the count and the kind).</summary>
    /// <exception cref="ReplicationItemException">The peer refused or answered something that is not an inventory.</exception>
    public async Task<ReplicationInventory> GetInventoryAsync(bool summary, CancellationToken cancellationToken)
    {
        var url = Root("api/v1/replication/inventory/" + Uri.EscapeDataString(Repository) + (summary ? "?summary=true" : string.Empty));
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ReplicationItemException(response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => "The peer refused the API key (401). It needs the admin:api scope of an administrator.",
                System.Net.HttpStatusCode.Forbidden => "The peer refused the API key (403). It needs the admin:api scope of an administrator.",
                System.Net.HttpStatusCode.NotFound => $"The peer has no repository '{Repository}' (or is not a BaGet server with replication).",
                _ => $"The peer answered {(int)response.StatusCode} to the inventory request.",
            });
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<ReplicationInventory>(Json, cancellationToken).ConfigureAwait(false)
                ?? throw new ReplicationItemException("The peer's inventory is empty.");
        }
        catch (JsonException e)
        {
            throw new ReplicationItemException("The peer did not answer with an inventory.", inner: e);
        }
    }

    /// <summary>Fails with a message (never the credential) unless <paramref name="response"/> succeeded.</summary>
    /// <param name="response">The peer's answer.</param>
    /// <param name="what">What was asked, for the message (<c>push</c>, <c>download</c>).</param>
    /// <param name="item">The item.</param>
    public static async Task EnsureAsync(HttpResponseMessage response, string what, ReplicaItem item)
    {
        if (response.IsSuccessStatusCode) return;

        var body = string.Empty;
        try
        {
            body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or InvalidOperationException)
        {
            // The status alone is the message.
        }

        body = body.Length > 200 ? body.Substring(0, 200) : body;
        throw new ReplicationItemException(
            $"The peer answered {(int)response.StatusCode} to the {what} of {item}{(string.IsNullOrWhiteSpace(body) ? "." : ": " + body.Trim())}",
            conflict: response.StatusCode == System.Net.HttpStatusCode.Conflict);
    }

    public void Dispose()
    {
        if (_ownsClient) Http.Dispose();
    }

    /// <summary>The serializer options of the inventory (shared with the API's response).</summary>
    public static JsonSerializerOptions JsonOptions => Json;

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}

/// <summary>
/// The seam that lets a background job run once cluster-wide. Replication takes the lease named after a configuration before every
/// run; the in-process default (a single node) is replaced by <see cref="ClusterJobLease"/> (a lease in the shared store) when <c>Ha:Enabled</c>, so two nodes do not run the same job at once. Retention, scanning, the staging sweep, validation and the admin operations take leases through the same service.
/// </summary>
public interface IJobLease
{
    /// <summary>Tries to take the lease for <paramref name="job"/>.</summary>
    /// <param name="job">The job's name (<c>replication:{configuration key}</c>).</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>The held lease (dispose it to release), or null when another holder has it: the caller skips the run.</returns>
    ValueTask<IAsyncDisposable?> TryAcquireAsync(string job, CancellationToken cancellationToken);
}

/// <summary>The single-node <see cref="IJobLease"/>: a lease is held until it is disposed, in this process only.</summary>
public sealed class InProcessJobLease : IJobLease
{
    private readonly object _lock = new();
    private readonly HashSet<string> _held = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public ValueTask<IAsyncDisposable?> TryAcquireAsync(string job, CancellationToken cancellationToken)
    {
        if (job == null) throw new ArgumentNullException(nameof(job));

        lock (_lock)
        {
            if (!_held.Add(job)) return new ValueTask<IAsyncDisposable?>((IAsyncDisposable?)null);
        }

        return new ValueTask<IAsyncDisposable?>(new Lease(this, job));
    }

    private sealed class Lease : IAsyncDisposable
    {
        private InProcessJobLease? _owner;
        private readonly string _job;

        public Lease(InProcessJobLease owner, string job)
        {
            _owner = owner;
            _job = job;
        }

        public ValueTask DisposeAsync()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner != null)
            {
                lock (owner._lock) owner._held.Remove(_job);
            }

            return default;
        }
    }
}
