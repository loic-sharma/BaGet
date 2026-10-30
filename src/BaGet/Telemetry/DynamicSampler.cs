using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using BaGet.Core;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;

namespace BaGet.Telemetry;

/// <summary>
/// <c>ParentBased(TraceIdRatio(ratio))</c> while traces are exported, <c>Drop</c> otherwise. The options are read on
/// every call (the monitor caches them), so the Settings page switches tracing on and off without a restart; while off,
/// no span is recorded or queued and nothing reaches the network.
/// </summary>
internal sealed class DynamicSampler : Sampler
{
    private readonly IOptionsMonitor<TelemetryOptions> _options;
    private readonly ConcurrentDictionary<double, TraceIdRatioBasedSampler> _ratios = new();

    public DynamicSampler(IOptionsMonitor<TelemetryOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        Description = "BaGetDynamicSampler";
    }

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        var options = TelemetryEndpoints.Read(_options);
        if (options == null || TelemetryEndpoints.Resolve(options, TelemetrySignal.Traces) == null)
        {
            return new SamplingResult(SamplingDecision.Drop);
        }

        var parent = samplingParameters.ParentContext;
        if (parent.TraceId != default)
        {
            // Follow the caller's decision (ParentBased).
            return new SamplingResult((parent.TraceFlags & ActivityTraceFlags.Recorded) != 0
                ? SamplingDecision.RecordAndSample
                : SamplingDecision.Drop);
        }

        var ratio = Math.Min(1.0, Math.Max(0.0, options.Traces?.SamplingRatio ?? 1.0));
        if (ratio >= 1.0) return new SamplingResult(SamplingDecision.RecordAndSample);
        if (ratio <= 0.0) return new SamplingResult(SamplingDecision.Drop);

        return _ratios.GetOrAdd(ratio, r => new TraceIdRatioBasedSampler(r)).ShouldSample(samplingParameters);
    }
}
