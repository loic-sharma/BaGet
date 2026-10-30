using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>
/// The small shared key/value store a cluster of nodes coordinates through (Redis in production, <see cref="InMemoryClusterStore"/>
/// in tests). Values are short strings with an optional time to live. Registered only when <c>Ha:Enabled</c> is set.
/// </summary>
public interface IClusterStore
{
    /// <summary>Where it is, without credentials (shown on the cluster page).</summary>
    string Description { get; }

    /// <summary>Whether the store answers.</summary>
    Task<bool> PingAsync(CancellationToken cancellationToken);

    /// <summary>The value, or null when absent or expired.</summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>Sets the value, replacing any; <paramref name="ttl"/> null keeps it until deleted.</summary>
    Task SetAsync(string key, string value, TimeSpan? ttl, CancellationToken cancellationToken);

    /// <summary>Sets the value only when the key is absent (atomic). False when it exists.</summary>
    Task<bool> SetIfAbsentAsync(string key, string value, TimeSpan ttl, CancellationToken cancellationToken);

    /// <summary>Deletes the key. False when it did not exist.</summary>
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken);

    /// <summary>Restarts the time to live of the key when it still holds <paramref name="value"/> (atomic). False otherwise.</summary>
    Task<bool> ExtendIfValueAsync(string key, string value, TimeSpan ttl, CancellationToken cancellationToken);

    /// <summary>Deletes the key when it still holds <paramref name="value"/> (atomic). False otherwise.</summary>
    Task<bool> DeleteIfValueAsync(string key, string value, CancellationToken cancellationToken);

    /// <summary>Adds one to the counter (created as 1 with <paramref name="ttlOnCreate"/>, which later increments do not change) and returns it.</summary>
    Task<long> IncrementAsync(string key, TimeSpan ttlOnCreate, CancellationToken cancellationToken);

    /// <summary>The live keys that start with <paramref name="prefix"/> and their values.</summary>
    Task<IReadOnlyDictionary<string, string>> ListAsync(string prefix, CancellationToken cancellationToken);
}

/// <summary>Blocking forms for the few synchronous callers (the rate limiters, the read-only gate); the calls are single round trips.</summary>
internal static class ClusterStoreExtensions
{
    public static bool SetIfAbsent(this IClusterStore store, string key, string value, TimeSpan ttl)
        => store.SetIfAbsentAsync(key, value, ttl, CancellationToken.None).GetAwaiter().GetResult();

    public static void Set(this IClusterStore store, string key, string value, TimeSpan? ttl)
        => store.SetAsync(key, value, ttl, CancellationToken.None).GetAwaiter().GetResult();

    public static void Delete(this IClusterStore store, string key)
        => store.DeleteAsync(key, CancellationToken.None).GetAwaiter().GetResult();

    public static long Increment(this IClusterStore store, string key, TimeSpan ttlOnCreate)
        => store.IncrementAsync(key, ttlOnCreate, CancellationToken.None).GetAwaiter().GetResult();
}
