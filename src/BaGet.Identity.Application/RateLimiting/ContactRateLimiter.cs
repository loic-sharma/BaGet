using System;
using System.Collections.Concurrent;
using System.Linq;
using BaGet.Core;

namespace BaGet.Identity.Application.RateLimiting;

/// <summary>
/// Allows one contact-owners message per sender per package per <see cref="Window"/>.
/// </summary>
/// <remarks>
/// The state is in memory, so it is per process: it resets on restart and each instance of a
/// scaled-out deployment counts separately. That bounds spam well enough for a feed's
/// size and keeps the feature free of schema changes. Time comes from <see cref="SystemTime"/>.
/// In a cluster (<c>Ha:Enabled</c>) the allowance is a key in the shared store instead (expiring after <see cref="Window"/>), so
/// the nodes count together.
/// </remarks>
public class ContactRateLimiter
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private readonly SystemTime _time;
    private readonly ConcurrentDictionary<(string Sender, string PackageId), DateTime> _last = new();
    private readonly object _gate = new();

    private readonly IClusterStore? _shared;

    public ContactRateLimiter(SystemTime time, ClusterRuntime? cluster = null)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _shared = cluster?.Store;
    }

    private static string SharedKey(string senderId, string? packageId) => "limit:contact:" + senderId + ":" + packageId?.ToLowerInvariant();

    /// <summary>
    /// Takes the sender's allowance for the package, or returns false when it was used less
    /// than <see cref="Window"/> ago. Atomic, so two concurrent posts cannot both pass.
    /// </summary>
    public bool TryAcquire(string senderId, string? packageId)
    {
        if (_shared != null) return _shared.SetIfAbsent(SharedKey(senderId, packageId), "1", Window);

        var now = _time.UtcNow;
        var key = (senderId, packageId?.ToLowerInvariant());

        lock (_gate)
        {
            #pragma warning disable CS8620
            if (_last.TryGetValue(key, out var previous) && now - previous < Window)
            #pragma warning restore CS8620
            {
                return false;
            }

            #pragma warning disable CS8620
            _last[key] = now;
            #pragma warning restore CS8620
            if (_last.Count > 10_000) Prune(now);

            return true;
        }
    }

    /// <summary>
    /// Gives the allowance back, when the message could not be sent.
    /// </summary>
    public void Release(string senderId, string? packageId)
    {
        if (_shared != null)
        {
            _shared.Delete(SharedKey(senderId, packageId));

            return;
        }

        #pragma warning disable CS8620
        lock (_gate) _last.TryRemove((senderId, packageId?.ToLowerInvariant()), out _);
        #pragma warning restore CS8620
    }

    private void Prune(DateTime now)
    {
        foreach (var expired in _last.Where(p => now - p.Value >= Window).Select(p => p.Key).ToList())
        {
            _last.TryRemove(expired, out _);
        }
    }
}
