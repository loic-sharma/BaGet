using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol;

namespace BaGet.Core;

/// <summary>
/// What the cache of one remote repository looks like right now (in memory, per node; resets on restart).
/// </summary>
/// <param name="MetadataEntries">Cached answers of the upstream (version lists and metadata), expired ones included until used or purged.</param>
/// <param name="NegativeEntries">Remembered "the upstream does not have it" answers still in force.</param>
/// <param name="Hits">Answers served from the cache.</param>
/// <param name="Misses">Questions that went to the upstream.</param>
/// <param name="NegativeHits">Questions answered "not found" from the negative cache.</param>
/// <param name="StaleServed">Expired answers served because the upstream could not be reached.</param>
/// <param name="Failures">Failed attempts to reach the upstream.</param>
/// <param name="LastSuccessUtc">The last time the upstream answered.</param>
/// <param name="LastFailureUtc">The last time it could not be reached.</param>
/// <param name="FailingSinceUtc">When the current run of failures began, or null while it answers.</param>
/// <param name="LastError">What failed, without URLs or credentials.</param>
public sealed record RemoteCacheStatus(
    int MetadataEntries,
    int NegativeEntries,
    long Hits,
    long Misses,
    long NegativeHits,
    long StaleServed,
    long Failures,
    DateTime? LastSuccessUtc,
    DateTime? LastFailureUtc,
    DateTime? FailingSinceUtc,
    string? LastError);

/// <summary>
/// The in-memory state of the remote repositories, shared by every request: parsed settings (so a request does not
/// read the database for them), cached answers of the upstreams, counters and pooled HTTP clients. A singleton.
/// </summary>
/// <remarks>
/// It is keyed by the key of the repository whose upstream it is (for the default repository's mirror, the
/// <c>mirror</c> repository's). Nothing here is shared between nodes: another node sees a settings change within
/// <see cref="SettingsLifetime"/>, and each node has its own cache.
/// </remarks>
public sealed class RemoteRepositoryState
{
    /// <summary>How long parsed settings are reused before the database is read again.</summary>
    public static readonly TimeSpan SettingsLifetime = TimeSpan.FromSeconds(5);

    /// <summary>The most cached answers kept per repository.</summary>
    public const int MaxEntries = 20000;

    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<int, Entry> _entries = new();

    public RemoteRepositoryState(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The current time (UTC).</summary>
    public DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    private Entry For(int key) => _entries.GetOrAdd(key, _ => new Entry());

    /// <summary>
    /// A value looked up for repository <paramref name="key"/> (its parsed settings, or for <see cref="MirrorLookupKey"/>
    /// which repository is the mirror), from the cache or else <paramref name="load"/>.
    /// </summary>
    internal async Task<T?> GetCachedAsync<T>(int key, Func<Task<T?>> load)
        where T : class
    {
        var entry = For(key);
        var cached = entry.Settings;
        if (cached != null && UtcNow - cached.LoadedUtc < SettingsLifetime && cached.Value is T or null) return (T?)cached.Value;

        var loaded = await load().ConfigureAwait(false);
        entry.Settings = new LoadedSettings(loaded, UtcNow);

        return loaded;
    }

    /// <summary>The pseudo key under which the lookup of the mirror repository is cached; no repository has it.</summary>
    internal const int MirrorLookupKey = -1;

    /// <summary>Forgets the parsed settings and the pooled client of a repository (after its settings changed).</summary>
    public void InvalidateSettings(int key)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            entry.Settings = null;
            entry.Transport = null;
        }
    }

    /// <summary>Forgets everything about a repository (it was deleted).</summary>
    public void Remove(int key) => _entries.TryRemove(key, out _);

    /// <summary>
    /// The pooled transport for <paramref name="settings"/>: rebuilt only when something that affects how the upstream
    /// is contacted changed, so a V3 service index is not fetched again for every request.
    /// </summary>
    internal UpstreamTransport GetTransport(int key, RemoteRepositorySettings settings, Func<UpstreamTransport> create)
    {
        var entry = For(key);
        var transportKey = settings.TransportKey();
        var current = entry.Transport;
        if (current != null && current.Key == transportKey) return current;

        lock (entry)
        {
            current = entry.Transport;
            if (current != null && current.Key == transportKey) return current;

            entry.Transport = current = create();

            return current;
        }
    }

    /// <summary>The cached value for <paramref name="cacheKey"/>, with whether it is still fresh.</summary>
    internal bool TryGet(int key, string cacheKey, out object? value, out bool negative, out bool fresh)
    {
        value = null;
        negative = false;
        fresh = false;

        if (!For(key).Metadata.TryGetValue(cacheKey, out var item)) return false;

        value = item.Value;
        negative = item.Negative;
        fresh = item.ExpiresUtc > UtcNow;

        return true;
    }

    /// <summary>
    /// Remembers an answer for <paramref name="ttl"/>. Nothing is kept for a zero period. At most <paramref name="limit"/>
    /// answers are kept per repository (the oldest go first): an npm packument can be megabytes, a NuGet version list is small.
    /// </summary>
    internal void Set(int key, string cacheKey, object? value, bool negative, TimeSpan ttl, int limit = MaxEntries)
    {
        if (ttl <= TimeSpan.Zero) return;

        var entry = For(key);
        if (entry.Metadata.Count >= limit) Trim(entry, limit);

        // An expired entry stays so it can be served while the upstream is down, until purged or trimmed.
        entry.Metadata[cacheKey] = new Item(value, negative, UtcNow + ttl);
    }

    private void Trim(Entry entry, int limit)
    {
        var now = UtcNow;
        foreach (var pair in entry.Metadata.Where(p => p.Value.ExpiresUtc <= now).Take(Math.Max(limit / 4, 1)).ToList())
        {
            entry.Metadata.TryRemove(pair.Key, out _);
        }

        if (entry.Metadata.Count < limit) return;

        foreach (var pair in entry.Metadata.OrderBy(p => p.Value.ExpiresUtc).Take(Math.Max(limit / 4, 1)).ToList())
        {
            entry.Metadata.TryRemove(pair.Key, out _);
        }
    }

    internal void Count(int key, CacheEvent kind)
    {
        var entry = For(key);
        switch (kind)
        {
            case CacheEvent.Hit: Interlocked.Increment(ref entry.Hits); break;
            case CacheEvent.Miss: Interlocked.Increment(ref entry.Misses); break;
            case CacheEvent.NegativeHit: Interlocked.Increment(ref entry.NegativeHits); break;
            case CacheEvent.Stale: Interlocked.Increment(ref entry.Stale); break;
        }
    }

    /// <summary>Records that the upstream answered.</summary>
    internal void Succeeded(int key)
    {
        var entry = For(key);
        lock (entry)
        {
            entry.LastSuccess = UtcNow;
            entry.FailingSince = null;
        }
    }

    /// <summary>Records that the upstream could not be reached; <paramref name="error"/> is shown on the status page.</summary>
    internal void Failed(int key, string error)
    {
        var entry = For(key);
        Interlocked.Increment(ref entry.Failures);
        lock (entry)
        {
            var now = UtcNow;
            entry.LastFailure = now;
            entry.FailingSince ??= now;
            entry.LastError = error;
        }
    }

    /// <summary>The cache status of a repository.</summary>
    public RemoteCacheStatus Status(int key)
    {
        if (!_entries.TryGetValue(key, out var entry))
        {
            return new RemoteCacheStatus(0, 0, 0, 0, 0, 0, 0, null, null, null, null);
        }

        var now = UtcNow;
        var items = entry.Metadata.Values;
        lock (entry)
        {
            return new RemoteCacheStatus(
                items.Count(i => !i.Negative),
                items.Count(i => i.Negative && i.ExpiresUtc > now),
                Interlocked.Read(ref entry.Hits),
                Interlocked.Read(ref entry.Misses),
                Interlocked.Read(ref entry.NegativeHits),
                Interlocked.Read(ref entry.Stale),
                Interlocked.Read(ref entry.Failures),
                entry.LastSuccess,
                entry.LastFailure,
                entry.FailingSince,
                entry.LastError);
        }
    }

    /// <summary>Drops every cached answer of a repository. Counters stay.</summary>
    /// <returns>How many answers were dropped.</returns>
    public int Clear(int key)
    {
        if (!_entries.TryGetValue(key, out var entry)) return 0;

        var count = entry.Metadata.Count;
        entry.Metadata.Clear();

        return count;
    }

    /// <summary>Drops the cached answers about one package id (version lists, metadata, 404s of any version).</summary>
    /// <returns>How many answers were dropped.</returns>
    public int ClearPackage(int key, string packageId)
    {
        if (!_entries.TryGetValue(key, out var entry)) return 0;

        var id = packageId.ToLowerInvariant();
        var removed = 0;
        foreach (var cacheKey in entry.Metadata.Keys.ToList())
        {
            // Keys are "<kind>:<id>" or "<kind>:<id>/<version>".
            var rest = cacheKey.Substring(cacheKey.IndexOf(':') + 1);
            var name = rest.Contains('/') ? rest.Substring(0, rest.IndexOf('/')) : rest;
            if (name == id && entry.Metadata.TryRemove(cacheKey, out _)) removed++;
        }

        return removed;
    }

    /// <summary>
    /// Drops the cached answers whose key satisfies <paramref name="match"/> (a format whose keys are paths, such as Maven's, cannot
    /// use <see cref="ClearPackage"/>'s name split).
    /// </summary>
    /// <returns>How many answers were dropped.</returns>
    public int ClearMatching(int key, Func<string, bool> match)
    {
        if (!_entries.TryGetValue(key, out var entry)) return 0;

        var removed = 0;
        foreach (var cacheKey in entry.Metadata.Keys.ToList())
        {
            if (match(cacheKey) && entry.Metadata.TryRemove(cacheKey, out _)) removed++;
        }

        return removed;
    }

    internal enum CacheEvent { Hit, Miss, NegativeHit, Stale }

    private sealed record LoadedSettings(object? Value, DateTime LoadedUtc);

    private sealed record Item(object? Value, bool Negative, DateTime ExpiresUtc);

    private sealed class Entry
    {
        public readonly ConcurrentDictionary<string, Item> Metadata = new();
        public volatile LoadedSettings? Settings;
        public volatile UpstreamTransport? Transport;
        public long Hits, Misses, NegativeHits, Stale, Failures;
        public DateTime? LastSuccess, LastFailure, FailingSince;
        public string? LastError;
    }
}

/// <summary>
/// The HTTP machinery for one upstream, pooled by <see cref="RemoteRepositoryState"/>.
/// </summary>
internal sealed class UpstreamTransport
{
    public UpstreamTransport(string key, HttpClient? http, NuGetClient? v3)
    {
        Key = key;
        Http = http;
        V3 = v3;
    }

    public string Key { get; }

    public HttpClient? Http { get; }

    public NuGetClient? V3 { get; }

    /// <summary>
    /// Bearer tokens the upstream's token service issued, by scope (a Docker upstream); they live as long as the transport, which
    /// is rebuilt when the URL or the credentials change, so a token never outlives the settings it was obtained with.
    /// </summary>
    internal ConcurrentDictionary<string, UpstreamToken> Tokens { get; } = new();

    /// <summary>Set once the upstream has answered a request with a Basic challenge: the credentials are then sent to its host at once.</summary>
    internal volatile bool BasicChallenged;
}

/// <summary>A bearer token and when it stops being valid.</summary>
/// <param name="Value">The token.</param>
/// <param name="ExpiresUtc">When to ask for another.</param>
internal sealed record UpstreamToken(string Value, DateTime ExpiresUtc);
