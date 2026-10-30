using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using BaGet.Core;

namespace BaGet.Identity.Application.Services.Sessions;

/// <summary>
/// Remembers the exchange tokens of the external sign-in that were used, until they would have expired anyway, so each works once (the tokens themselves are stateless).
/// Only a hash is kept. In a cluster (<c>Ha:Enabled</c>) the shared store is used, otherwise memory (per process).
/// </summary>
public sealed class ExchangeTokenLedger
{
    private readonly SystemTime _time;
    private readonly IClusterStore? _shared;
    private readonly ConcurrentDictionary<string, DateTime> _used = new();

    /// <summary>Creates the ledger.</summary>
    public ExchangeTokenLedger(SystemTime time, ClusterRuntime? cluster = null)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _shared = cluster?.Store;
    }

    /// <summary>Records the token as used; false when it was used before.</summary>
    /// <param name="token">The exchange token.</param>
    /// <param name="expiresAt">When it expires: the record is kept until then.</param>
    public bool TryUse(string token, DateTime expiresAt)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var now = _time.UtcNow;
        var keep = expiresAt - now + TimeSpan.FromSeconds(5);
        if (keep < TimeSpan.FromSeconds(1)) keep = TimeSpan.FromSeconds(1);

        if (_shared != null) return _shared.SetIfAbsent("exchange:" + key, "1", keep);

        if (_used.Count > 1024)
        {
            foreach (var pair in _used)
            {
                if (pair.Value <= now) _used.TryRemove(pair.Key, out _);
            }
        }

        return _used.TryAdd(key, expiresAt + TimeSpan.FromSeconds(5));
    }
}
