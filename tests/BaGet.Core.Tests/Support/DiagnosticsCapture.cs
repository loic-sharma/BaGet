using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using Xunit;

namespace BaGet.Core.Tests.Support;

/// <summary>One counter increment seen by <see cref="DiagnosticsCapture"/>.</summary>
public sealed record CapturedMeasurement(string Instrument, long Value, IReadOnlyDictionary<string, object?> Tags);

/// <summary>
/// Listens to the "BaGet" <see cref="ActivitySource"/> and <see cref="Meter"/> for the life of the object. The sources are
/// process-wide, so tests that count measurements join the <see cref="Name"/> collection (it runs alone); tests that look
/// at spans filter by a package id of their own.
/// </summary>
public sealed class DiagnosticsCapture : IDisposable
{
    /// <summary>The xUnit collection (parallelization disabled) for tests that count measurements.</summary>
    public const string Name = "Diagnostics";

    private readonly ActivityListener _activities;
    private readonly MeterListener _meters = new();

    public DiagnosticsCapture()
    {
        _activities = new ActivityListener
        {
            ShouldListenTo = source => source.Name == BaGetDiagnostics.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = Stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(_activities);

        _meters.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == BaGetDiagnostics.Name) listener.EnableMeasurementEvents(instrument);
        };
        _meters.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            Measurements.Enqueue(new CapturedMeasurement(
                instrument.Name, value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
        _meters.Start();
    }

    public ConcurrentQueue<Activity> Stopped { get; } = new();

    public ConcurrentQueue<CapturedMeasurement> Measurements { get; } = new();

    public IReadOnlyList<Activity> Spans(string name) => Stopped.Where(a => a.OperationName == name).ToList();

    public IReadOnlyList<CapturedMeasurement> Counts(string instrument)
        => Measurements.Where(m => m.Instrument == instrument).ToList();

    public static object? Tag(Activity activity, string key) => activity.GetTagItem(key);

    public void Dispose()
    {
        _activities.Dispose();
        _meters.Dispose();
    }
}

[CollectionDefinition(DiagnosticsCapture.Name, DisableParallelization = true)]
public class DiagnosticsCollection
{
}
