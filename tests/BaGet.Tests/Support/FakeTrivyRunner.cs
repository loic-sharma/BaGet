using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;

namespace BaGet.Tests;

/// <summary>Stands in for the <c>trivy</c> executable: reports the findings a test registers and records what it was asked to scan.</summary>
public sealed class FakeTrivyRunner : ITrivyRunner
{
    private readonly ConcurrentQueue<string> _references = new();

    /// <summary>The findings reported for every image.</summary>
    public List<ScanHit> Hits { get; } = new();

    /// <summary>When set, every scan fails with this message.</summary>
    public string? Failure { get; set; }

    /// <summary>Runs during a scan, with the image reference (a test pulls the image through the registry here, like Trivy does).</summary>
    public Func<string, Task>? OnScan { get; set; }

    /// <summary>The image references scanned so far, in order.</summary>
    public string[] References => _references.ToArray();

    /// <inheritdoc/>
    public bool IsConfigured(ScanningOptions options)
        => !string.IsNullOrWhiteSpace(options.TrivyPath) && !string.IsNullOrWhiteSpace(options.TrivyRegistryHost);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ScanHit>> ScanImageAsync(ScanningOptions options, string imageReference, CancellationToken cancellationToken)
    {
        _references.Enqueue(imageReference);
        if (Failure != null) throw new ScanUnavailableException(Failure);
        if (OnScan != null) await OnScan(imageReference);

        return Hits.ToList();
    }
}
