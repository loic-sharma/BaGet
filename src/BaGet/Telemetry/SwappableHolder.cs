using System;
using System.Threading;

namespace BaGet.Telemetry;

/// <summary>
/// Holds one disposable value that can be replaced while other threads are using it. A user takes a
/// <see cref="Lease"/> for the duration of a call; a replaced (retired) value is disposed exactly once, when the last
/// lease on it is released, so an export in flight is never cut off and a value is never disposed twice or while in use.
/// </summary>
internal sealed class SwappableHolder<T> : IDisposable where T : class, IDisposable
{
    private readonly object _gate = new();
    private Lease? _current;
    private bool _closed;

    /// <summary>The current value leased, or null when there is none (signal off) or the holder is closed.</summary>
    public Lease? Acquire()
    {
        lock (_gate)
        {
            if (_current == null) return null;

            _current.Take();
            return _current;
        }
    }

    /// <summary>The current value without leasing it. For tests and diagnostics only.</summary>
    public T? Current
    {
        get
        {
            lock (_gate) return _current?.Value;
        }
    }

    /// <summary>Replace the value (null for none). The previous value is disposed once its users are done.</summary>
    public void Swap(T? next)
    {
        Lease? old;
        lock (_gate)
        {
            if (_closed)
            {
                // Shut down already: nothing may be started again.
                next?.Dispose();
                return;
            }

            old = _current;
            _current = next == null ? null : new Lease(next);
        }

        old?.Retire();
    }

    /// <summary>Retire the current value and refuse any later <see cref="Swap"/>. Idempotent.</summary>
    public void Close()
    {
        Lease? old;
        lock (_gate)
        {
            _closed = true;
            old = _current;
            _current = null;
        }

        old?.Retire();
    }

    public void Dispose() => Close();

    /// <summary>A use of a value; dispose it to release.</summary>
    public sealed class Lease : IDisposable
    {
        private readonly object _lock = new();
        private int _users;
        private bool _retired;
        private int _disposed;

        internal Lease(T value) => Value = value;

        public T Value { get; }

        internal void Take()
        {
            lock (_lock) _users++;
        }

        internal void Retire()
        {
            bool dispose;
            lock (_lock)
            {
                _retired = true;
                dispose = _users == 0;
            }

            if (dispose) DisposeValue();
        }

        public void Dispose()
        {
            bool dispose;
            lock (_lock)
            {
                _users--;
                dispose = _retired && _users == 0;
            }

            if (dispose) DisposeValue();
        }

        private void DisposeValue()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            try
            {
                Value.Dispose();
            }
            catch
            {
                // Best effort: a failing exporter must not take the host down.
            }
        }
    }
}
