using System;
using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Identity.Application.RateLimiting;

/// <summary>
/// Limits "forgot password" mail: one per account per <see cref="Window"/> (the real
/// protection), plus a generous backstop of <see cref="MaxPerClient"/> per client address per
/// <see cref="Window"/>. There is deliberately no site-wide cap: it would let one caller, with a
/// spoofed address and public account names, block everyone else's reset.
/// </summary>
/// <remarks>
/// <para>
/// The client address is only as good as the forwarded headers the server trusts (they are
/// trusted from anyone, see <c>ConfigureBaGetOptions</c>), so it can be spoofed, and behind a
/// proxy that sets no forwarded header every request shares one address. That is why the
/// client limit is high and is not the only gate.
/// </para>
/// <para>
/// Only allowed mail is recorded, and the caller asks only for accounts that exist, so the
/// state holds at most one entry per account (an account cannot be acquired twice within the
/// window) and at most as many client entries: it is bounded by the number of accounts, whatever
/// the traffic. Entries leave in the order they came in, so expiry is O(1) amortised with no scan,
/// and a client's count only ever drops with age, which can make the limiter more permissive
/// for that client, never block an account. Like <see cref="ContactRateLimiter"/> the state is in
/// memory: per process, reset on restart. The caller answers identically whether or not a request
/// was allowed. In a cluster (<c>Ha:Enabled</c>) the account allowance is a key in the shared store (expiring after <see cref="Window"/>)
/// and the client count a shared counter that starts its <see cref="Window"/> at the first request (a fixed window, where the in-memory
/// one slides), so the nodes count together.
/// </para>
/// </remarks>
public class PasswordResetRateLimiter
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    public const int MaxPerClient = 60;

    private readonly SystemTime _time;
    private readonly Queue<(DateTime At, string Client, string Target)> _log = new();
    private readonly Dictionary<string, DateTime> _targets = new();
    private readonly Dictionary<string, int> _clients = new();
    private readonly object _gate = new();

    /// <summary>What is being remembered; never more than the number of accounts.</summary>
    internal int TrackedCount
    {
        get
        {
            lock (_gate) return _log.Count;
        }
    }

    private readonly IClusterStore? _shared;

    public PasswordResetRateLimiter(SystemTime time, ClusterRuntime? cluster = null)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _shared = cluster?.Store;
    }

    /// <summary>
    /// Takes the allowance, or returns false when the target was mailed less than
    /// <see cref="Window"/> ago, or the client has used up its requests. Atomic.
    /// </summary>
    public bool TryAcquire(string client, string target)
    {
        if (_shared != null) return TryAcquireShared(client, target);

        var now = _time.UtcNow;

        lock (_gate)
        {
            Expire(now);

            if (_targets.ContainsKey(target)) return false;
            if (_clients.TryGetValue(client, out var used) && used >= MaxPerClient) return false;

            _log.Enqueue((now, client, target));
            _targets[target] = now;
            _clients[client] = used + 1;

            return true;
        }
    }

    private bool TryAcquireShared(string client, string target)
    {
        var shared = _shared!;
        var targetKey = "limit:reset:target:" + target;
        if (!shared.SetIfAbsent(targetKey, "1", Window)) return false;

        if (shared.Increment("limit:reset:client:" + client, Window) > MaxPerClient)
        {
            // The client used its requests up: nothing was mailed, so the account keeps its allowance.
            shared.Delete(targetKey);

            return false;
        }

        return true;
    }

    private void Expire(DateTime now)
    {
        while (_log.Count > 0 && now - _log.Peek().At >= Window)
        {
            var (_, client, target) = _log.Dequeue();
            _targets.Remove(target);

            if (_clients[client] <= 1) _clients.Remove(client);
            else _clients[client]--;
        }
    }
}
