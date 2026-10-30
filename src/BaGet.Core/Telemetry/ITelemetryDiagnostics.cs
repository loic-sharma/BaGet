using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>What one telemetry signal is doing, as the Settings page shows it.</summary>
public enum TelemetrySignalState
{
    /// <summary>Telemetry or this signal is switched off.</summary>
    Off,

    /// <summary>Switched on but no usable endpoint is set.</summary>
    NotConfigured,

    /// <summary>Configured; nothing has been exported yet.</summary>
    Waiting,

    /// <summary>The last export succeeded.</summary>
    Ok,

    /// <summary>The last export failed.</summary>
    Failing,
}

/// <summary>One signal's state. <see cref="LastError"/> is already scrubbed (no headers, credentials or URL paths).</summary>
public sealed record TelemetrySignalReport(
    string Signal,
    TelemetrySignalState State,
    DateTimeOffset? LastSuccess,
    DateTimeOffset? FailingSince,
    string? LastError,
    long Dropped);

/// <summary>The outcome of a connection test; <see cref="Message"/> and <see cref="Target"/> are scrubbed.</summary>
/// <param name="Ok">The collector accepted a test span.</param>
/// <param name="Message">What happened.</param>
/// <param name="Target">The endpoint that was tested, cut to scheme, host and port.</param>
public sealed record TelemetryTestOutcome(bool Ok, string Message, string? Target);

/// <summary>
/// What the Settings page needs from the host's OpenTelemetry export (the exporters live in the host, which the
/// identity pages cannot reference). Absent when a host does not export telemetry.
/// </summary>
public interface ITelemetryDiagnostics
{
    /// <summary>The state of traces, metrics and logs.</summary>
    IReadOnlyList<TelemetrySignalReport> GetStatus();

    /// <summary>
    /// Sends one test span with explicit settings, saving and changing nothing. A null argument means the saved
    /// value (so a sensitive field left blank on the form keeps its secret); an empty one means none.
    /// </summary>
    Task<TelemetryTestOutcome> TestAsync(string? endpoint, string? protocol, string? headers, CancellationToken cancellationToken = default);
}
