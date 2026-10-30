using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using BaGet.Core;
using BaGet.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog.Core;
using Serilog.Events;

namespace BaGet.Tests;

/// <summary>A finished span, copied at export (an <see cref="Activity"/> is not safe to keep).</summary>
public sealed record ExportedSpan(
    string Name,
    string Source,
    ActivityKind Kind,
    string TraceId,
    string SpanId,
    string? ParentSpanId,
    IReadOnlyDictionary<string, string?> Tags);

/// <summary>
/// Replaces the OTLP exporters with in-memory ones so the integration tests can assert what the host would export,
/// through the real sampler, processors, reloadable exporters and Serilog pipeline. Install it with
/// <see cref="Install"/> in the harness's <c>configureServices</c>; enable export with
/// <c>Telemetry:Enabled=true</c> plus an endpoint (it is never contacted).
/// </summary>
internal sealed class InMemoryTelemetry : ITelemetryExporterFactory
{
    private readonly ConcurrentQueue<ExportedSpan> _spans = new();
    private readonly ConcurrentQueue<LogEvent> _logs = new();
    private readonly ConcurrentQueue<TelemetryOptions> _traceOptions = new();
    private int _traceExporters;
    private int _logSinks;

    public IReadOnlyList<ExportedSpan> Spans => _spans.ToArray();

    /// <summary>The options each trace exporter was built with, in order (a connection test builds one with the posted values).</summary>
    public IReadOnlyList<TelemetryOptions> TraceOptions => _traceOptions.ToArray();

    public IReadOnlyList<LogEvent> Logs => _logs.ToArray();

    /// <summary>How many trace exporters were built (a rebuild on change counts).</summary>
    public int TraceExportersCreated => Volatile.Read(ref _traceExporters);

    public int LogSinksCreated => Volatile.Read(ref _logSinks);

    public static InMemoryTelemetry Install(IServiceCollection services)
    {
        var telemetry = new InMemoryTelemetry();
        services.RemoveAll<ITelemetryExporterFactory>();
        services.AddSingleton<ITelemetryExporterFactory>(telemetry);
        return telemetry;
    }

    /// <summary>Pushes the queued spans to the exporter now (the batch processor otherwise waits for its timer).</summary>
    public static void Flush(IServiceProvider services)
    {
        services.GetRequiredService<TracerProvider>().ForceFlush(10_000);
        services.GetRequiredService<MeterProvider>().ForceFlush(10_000);
    }

    public BaseExporter<Activity> CreateTraceExporter(TelemetryOptions options, Uri endpoint)
    {
        Interlocked.Increment(ref _traceExporters);
        _traceOptions.Enqueue(options);
        return new SpanExporter(_spans);
    }

    public BaseExporter<Metric> CreateMetricExporter(TelemetryOptions options, Uri endpoint) => new NoMetricExporter();

    public ILogEventSink CreateLogSink(TelemetryOptions options, Uri endpoint, string serviceName, TelemetryStatus.Signal status)
    {
        Interlocked.Increment(ref _logSinks);
        return new LogSink(_logs);
    }

    private sealed class SpanExporter : BaseExporter<Activity>
    {
        private readonly ConcurrentQueue<ExportedSpan> _spans;

        public SpanExporter(ConcurrentQueue<ExportedSpan> spans) => _spans = spans;

        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
            {
                _spans.Enqueue(new ExportedSpan(
                    activity.DisplayName,
                    activity.Source.Name,
                    activity.Kind,
                    activity.TraceId.ToHexString(),
                    activity.SpanId.ToHexString(),
                    activity.ParentSpanId == default ? null : activity.ParentSpanId.ToHexString(),
                    activity.TagObjects.ToDictionary(t => t.Key, t => t.Value?.ToString())));
            }

            return ExportResult.Success;
        }
    }

    private sealed class NoMetricExporter : BaseExporter<Metric>
    {
        public override ExportResult Export(in Batch<Metric> batch) => ExportResult.Success;
    }

    private sealed class LogSink : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _logs;

        public LogSink(ConcurrentQueue<LogEvent> logs) => _logs = logs;

        public void Emit(LogEvent logEvent) => _logs.Enqueue(logEvent);
    }
}
