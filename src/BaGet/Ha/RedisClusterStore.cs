using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using StackExchange.Redis;

namespace BaGet.Ha;

/// <summary>
/// The Redis <see cref="IClusterStore"/>. Keys are written under <c>{Ha:KeyPrefix}:</c> and returned without it. The connection is
/// made on first use and does not abort when Redis is down at start (the node starts, its readiness check fails, and calls fail until
/// Redis is back; leases then refuse and the nodes keep serving). The connection string is never logged or shown.
/// </summary>
internal sealed class RedisClusterStore : IClusterStore, IAsyncDisposable
{
    private const string ExtendScript = "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('pexpire', KEYS[1], ARGV[2]) else return 0 end";
    private const string DeleteScript = "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";
    private const string IncrementScript = "local n = redis.call('incr', KEYS[1]); if n == 1 then redis.call('pexpire', KEYS[1], ARGV[1]) end; return n";

    private readonly ConfigurationOptions _options;
    private readonly string _prefix;
    private readonly Lazy<ConnectionMultiplexer> _connection;

    public RedisClusterStore(string connectionString, string keyPrefix)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("A Redis connection string is required.", nameof(connectionString));

        _options = ConfigurationOptions.Parse(connectionString);
        _options.AbortOnConnectFail = false;
        _prefix = keyPrefix.TrimEnd(':') + ":";
        _connection = new Lazy<ConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(_options), LazyThreadSafetyMode.ExecutionAndPublication);
        Description = "Redis " + string.Join(", ", _options.EndPoints.Select(e => e.ToString()));
    }

    public string Description { get; }

    private IDatabase Db => _connection.Value.GetDatabase();

    private RedisKey Key(string key) => _prefix + key;

    public async Task<bool> PingAsync(CancellationToken cancellationToken)
    {
        await Db.PingAsync().WaitAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        var value = await Db.StringGetAsync(Key(key)).WaitAsync(cancellationToken).ConfigureAwait(false);

        return value.HasValue ? (string?)value : null;
    }

    public Task SetAsync(string key, string value, TimeSpan? ttl, CancellationToken cancellationToken)
        => (ttl is TimeSpan expiry ? Db.StringSetAsync(Key(key), value, expiry) : Db.StringSetAsync(Key(key), value)).WaitAsync(cancellationToken);

    public Task<bool> SetIfAbsentAsync(string key, string value, TimeSpan ttl, CancellationToken cancellationToken)
        => Db.StringSetAsync(Key(key), value, ttl, When.NotExists).WaitAsync(cancellationToken);

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken)
        => Db.KeyDeleteAsync(Key(key)).WaitAsync(cancellationToken);

    public async Task<bool> ExtendIfValueAsync(string key, string value, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var result = await Db.ScriptEvaluateAsync(ExtendScript, new[] { Key(key) }, new RedisValue[] { value, (long)ttl.TotalMilliseconds }).WaitAsync(cancellationToken).ConfigureAwait(false);

        return (long)result == 1;
    }

    public async Task<bool> DeleteIfValueAsync(string key, string value, CancellationToken cancellationToken)
    {
        var result = await Db.ScriptEvaluateAsync(DeleteScript, new[] { Key(key) }, new RedisValue[] { value }).WaitAsync(cancellationToken).ConfigureAwait(false);

        return (long)result == 1;
    }

    public async Task<long> IncrementAsync(string key, TimeSpan ttlOnCreate, CancellationToken cancellationToken)
    {
        var result = await Db.ScriptEvaluateAsync(IncrementScript, new[] { Key(key) }, new RedisValue[] { (long)ttlOnCreate.TotalMilliseconds }).WaitAsync(cancellationToken).ConfigureAwait(false);

        return (long)result;
    }

    public async Task<IReadOnlyDictionary<string, string>> ListAsync(string prefix, CancellationToken cancellationToken)
    {
        var connection = _connection.Value;
        var server = connection.GetEndPoints()
            .Select(e => connection.GetServer(e))
            .FirstOrDefault(s => s.IsConnected && !s.IsReplica)
            ?? throw new InvalidOperationException("No Redis server is connected.");

        var keys = new List<RedisKey>();
        await foreach (var key in server.KeysAsync(pattern: Escape(_prefix + prefix) + "*").WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            keys.Add(key);
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (keys.Count == 0) return result;

        var values = await Db.StringGetAsync(keys.ToArray()).WaitAsync(cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < keys.Count; i++)
        {
            if (values[i].HasValue) result[((string)keys[i]!).Substring(_prefix.Length)] = values[i]!;
        }

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection.IsValueCreated) await _connection.Value.CloseAsync().ConfigureAwait(false);
    }

    /// <summary>Escapes the glob characters of a SCAN pattern.</summary>
    internal static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '*' or '?' or '[' or ']' or '\\') builder.Append('\\');
            builder.Append(c);
        }

        return builder.ToString();
    }
}
