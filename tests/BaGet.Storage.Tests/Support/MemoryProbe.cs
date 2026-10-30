using System;
using System.Diagnostics;
using System.Threading;

namespace BaGet.Storage.Tests;

/// <summary>Samples the process's private memory while a transfer runs, to show that it does not grow with the blob.</summary>
public sealed class MemoryProbe : IDisposable
{
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly Timer _timer;
    private long _peak;

    public MemoryProbe()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        _process.Refresh();
        Baseline = _process.PrivateMemorySize64;
        _peak = Baseline;
        _timer = new Timer(_ => Sample(), null, 0, 25);
    }

    /// <summary>Private bytes before the transfer.</summary>
    public long Baseline { get; }

    /// <summary>The largest growth over <see cref="Baseline"/> seen, in bytes.</summary>
    public long PeakGrowth
    {
        get
        {
            Sample();
            return Math.Max(0, Interlocked.Read(ref _peak) - Baseline);
        }
    }

    private void Sample()
    {
        try
        {
            _process.Refresh();
            var now = _process.PrivateMemorySize64;
            long seen;
            while (now > (seen = Interlocked.Read(ref _peak)))
            {
                if (Interlocked.CompareExchange(ref _peak, now, seen) == seen) break;
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void Dispose() => _timer.Dispose();
}
