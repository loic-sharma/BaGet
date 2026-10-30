using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>One holder per key at a time; a key's semaphore is dropped when nobody holds or waits for it.</summary>
internal sealed class KeyedLocks
{
    private readonly object _sync = new();
    private readonly Dictionary<string, (SemaphoreSlim Gate, int Users)> _entries = new();

    public async Task<IDisposable> LockAsync(string key, CancellationToken cancellationToken)
    {
        SemaphoreSlim gate;
        lock (_sync)
        {
            if (!_entries.TryGetValue(key, out var entry)) entry = (new SemaphoreSlim(1, 1), 0);
            _entries[key] = (entry.Gate, entry.Users + 1);
            gate = entry.Gate;
        }

        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Release(key, held: false);

            throw;
        }

        return new Releaser(this, key);
    }

    private void Release(string key, bool held)
    {
        lock (_sync)
        {
            var entry = _entries[key];
            if (held) entry.Gate.Release();
            if (entry.Users <= 1)
            {
                _entries.Remove(key);
            }
            else
            {
                _entries[key] = (entry.Gate, entry.Users - 1);
            }
        }
    }

    private sealed class Releaser : IDisposable
    {
        private readonly KeyedLocks _owner;
        private readonly string _key;
        private int _done;

        public Releaser(KeyedLocks owner, string key)
        {
            _owner = owner;
            _key = key;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0) _owner.Release(_key, held: true);
        }
    }
}
