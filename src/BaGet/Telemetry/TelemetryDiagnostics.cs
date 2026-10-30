using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.Options;

namespace BaGet.Telemetry;

/// <summary>The host's <see cref="ITelemetryDiagnostics"/>: <see cref="TelemetryStatus"/> and <see cref="TelemetryConnectionTester"/> for the Settings page.</summary>
internal sealed class TelemetryDiagnostics : ITelemetryDiagnostics
{
    private readonly IOptionsMonitor<TelemetryOptions> _options;
    private readonly TelemetryStatus _status;
    private readonly TelemetryConnectionTester _tester;

    public TelemetryDiagnostics(IOptionsMonitor<TelemetryOptions> options, TelemetryStatus status, TelemetryConnectionTester tester)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _tester = tester ?? throw new ArgumentNullException(nameof(tester));
    }

    public IReadOnlyList<TelemetrySignalReport> GetStatus()
    {
        var options = TelemetryEndpoints.Read(_options);
        var reports = new List<TelemetrySignalReport>();
        foreach (var signal in new[] { TelemetrySignal.Traces, TelemetrySignal.Metrics, TelemetrySignal.Logs })
        {
            var status = _status.Get(signal);
            reports.Add(new TelemetrySignalReport(signal.ToString(), StateOf(options, signal, status), status.LastSuccess, status.FailingSince, status.LastError, status.Dropped));
        }

        return reports;
    }

    private static TelemetrySignalState StateOf(TelemetryOptions? options, TelemetrySignal signal, TelemetrySignalStatus status)
    {
        if (options == null || !options.Enabled || (signal == TelemetrySignal.Traces && options.Traces?.Enabled == false))
        {
            return TelemetrySignalState.Off;
        }

        if (TelemetryEndpoints.Resolve(options, signal) == null) return TelemetrySignalState.NotConfigured;
        if (status.FailingSince != null) return TelemetrySignalState.Failing;

        return status.LastSuccess != null ? TelemetrySignalState.Ok : TelemetrySignalState.Waiting;
    }

    public async Task<TelemetryTestOutcome> TestAsync(string? endpoint, string? protocol, string? headers, CancellationToken cancellationToken = default)
    {
        var saved = TelemetryEndpoints.Read(_options)?.Otlp;
        endpoint ??= saved?.Endpoint;
        protocol ??= saved?.Protocol;
        headers ??= saved?.Headers;

        var result = await _tester.TestAsync(endpoint, protocol, headers, TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);

        // The tester scrubs against the headers it was given; scrub again so the message can never carry them.
        var target = string.IsNullOrWhiteSpace(endpoint) ? null : TelemetryStatus.Scrub(endpoint!.Trim(), headers);
        return new TelemetryTestOutcome(result.Ok, TelemetryStatus.Scrub(result.Message, headers), target);
    }
}
