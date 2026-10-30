using System;
using System.Diagnostics;
using BaGet.Core;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using Serilog.Core;

namespace BaGet.Telemetry;

/// <summary>
/// Builds the real exporters. A seam: the host uses <see cref="OtlpExporterFactory"/>; the integration tests replace it
/// with in-memory exporters so spans, metrics and logs can be asserted without a collector.
/// </summary>
internal interface ITelemetryExporterFactory
{
    BaseExporter<Activity> CreateTraceExporter(TelemetryOptions options, Uri endpoint);

    BaseExporter<Metric> CreateMetricExporter(TelemetryOptions options, Uri endpoint);

    /// <summary>
    /// The sink (disposed to flush and stop) that sends Serilog events to <paramref name="endpoint"/>, with
    /// <paramref name="serviceName"/> (the startup name, the same as the trace and metric resource) as <c>service.name</c>.
    /// </summary>
    ILogEventSink CreateLogSink(TelemetryOptions options, Uri endpoint, string serviceName, TelemetryStatus.Signal status);
}

/// <summary>OTLP over gRPC or HTTP/protobuf, as configured.</summary>
internal sealed class OtlpExporterFactory : ITelemetryExporterFactory
{
    public BaseExporter<Activity> CreateTraceExporter(TelemetryOptions options, Uri endpoint)
        => new OtlpTraceExporter(CreateOptions(options, endpoint));

    public BaseExporter<Metric> CreateMetricExporter(TelemetryOptions options, Uri endpoint)
        => new OtlpMetricExporter(CreateOptions(options, endpoint));

    public ILogEventSink CreateLogSink(TelemetryOptions options, Uri endpoint, string serviceName, TelemetryStatus.Signal status)
        => OpenTelemetryLogSink.Create(options, endpoint, serviceName, status);

    internal static OtlpExporterOptions CreateOptions(TelemetryOptions options, Uri endpoint)
    {
        var otlp = new OtlpExporterOptions
        {
            Endpoint = endpoint,
            Protocol = TelemetryEndpoints.IsGrpc(options) ? OtlpExportProtocol.Grpc : OtlpExportProtocol.HttpProtobuf,
            TimeoutMilliseconds = (int)Math.Min(int.MaxValue, Math.Max(1, (options.Otlp?.Timeout ?? TimeSpan.FromSeconds(10)).TotalMilliseconds)),
        };

        if (!string.IsNullOrWhiteSpace(options.Otlp?.Headers)) otlp.Headers = options.Otlp!.Headers;

        return otlp;
    }
}
