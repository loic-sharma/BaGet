using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>
/// An <see cref="IClusterStore"/> in this process's memory. Several hosts in one process can share one instance, which is how the
/// tests run a cluster without Redis; it is not a production backend (nothing outside the process sees it).
/// </summary>
public sealed class InMemoryClusterStore : IClusterStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, (string Value, DateTime? Expires)> _items = new(StringComparer.Ordinal);
    private readonly Func<DateTime> _clock;

    /// <summary>Creates a store.</summary>
    /// <param name="clock">UTC now; real time by default (tests pass a fake to expire entries).</param>
    public InMemoryClusterStore(Func<DateTime>? clock = null) => _clock = clock ?? (() => DateTime.UtcNow);

    /// <summary>Set to make every call fail, to test a node that loses the backend.</summary>
    public bool Unavailable { get; set; }

    /// <inheritdoc/>
    public string Description => "in-memory (single process)";

    /// <inheritdoc/>
    public Task<bool> PingAsync(CancellationToken cancellationToken) => Task.FromResult(!Unavailable);

    /// <inheritdoc/>
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        lock (_lock) return Task.FromResult(Live(key));
    }

    /// <inheritdoc/>
    public Task SetAsync(string key, string value, TimeSpan? ttl, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            Check();
            _items[key] = (value, ttl == null ? null : _clock() + ttl.Value);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> SetIfAbsentAsync(string key, string value, TimeSpan ttl, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (Live(key) != null) return Task.FromResult(false);
            _items[key] = (value, _clock() + ttl);

            return Task.FromResult(true);
        }
    }

    /// <inheritdoc/>
    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var existed = Live(key) != null;
            _items.Remove(key);

            return Task.FromResult(existed);
        }
    }

    /// <inheritdoc/>
    public Task<bool> ExtendIfValueAsync(string key, string value, TimeSpan ttl, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (Live(key) != value) return Task.FromResult(false);
            _items[key] = (value, _clock() + ttl);

            return Task.FromResult(true);
        }
    }

    /// <inheritdoc/>
    public Task<bool> DeleteIfValueAsync(string key, string value, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (Live(key) != value) return Task.FromResult(false);
            _items.Remove(key);

            return Task.FromResult(true);
        }
    }

    /// <inheritdoc/>
    public Task<long> IncrementAsync(string key, TimeSpan ttlOnCreate, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var current = Live(key);
            if (current == null)
            {
                _items[key] = ("1", _clock() + ttlOnCreate);

                return Task.FromResult(1L);
            }

            var next = long.Parse(current, CultureInfo.InvariantCulture) + 1;
            _items[key] = (next.ToString(CultureInfo.InvariantCulture), _items[key].Expires);

            return Task.FromResult(next);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyDictionary<string, string>> ListAsync(string prefix, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            Check();
            var now = _clock();
            var live = _items
                .Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal) && (p.Value.Expires == null || p.Value.Expires > now))
                .ToDictionary(p => p.Key, p => p.Value.Value, StringComparer.Ordinal);

            return Task.FromResult<IReadOnlyDictionary<string, string>>(live);
        }
    }

    private string? Live(string key)
    {
        Check();
        if (!_items.TryGetValue(key, out var item)) return null;
        if (item.Expires != null && item.Expires <= _clock())
        {
            _items.Remove(key);

            return null;
        }

        return item.Value;
    }

    private void Check()
    {
        if (Unavailable) throw new InvalidOperationException("The cluster store is unavailable.");
    }
}
