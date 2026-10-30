using System;
using System.Collections.Generic;

namespace BaGet.Core;

/// <summary>
/// OpenTelemetry export (traces, metrics, logs). Off by default. The libraries only emit
/// <see cref="BaGetDiagnostics"/> spans and metrics; the host exports them over OTLP.
/// The host reads these through <c>IOptionsMonitor</c> and applies a change live, except <see cref="ServiceName"/> and
/// <see cref="MetricsOptions.ExportInterval"/>, which are read once when the exporter is built
/// (<see cref="RestartRequiredAttribute"/>).
/// </summary>
public class TelemetryOptions
{
    /// <summary>The master switch. While false nothing is exported.</summary>
    public bool Enabled { get; set; }

    /// <summary>The <c>service.name</c> resource attribute reported with every signal.</summary>
    [RestartRequired]
    public string ServiceName { get; set; } = "baget";

    /// <summary>Where and how signals are sent (<see cref="OtlpOptions.Endpoint"/> is the traces endpoint; metrics and logs have their own endpoints and never fall back to it).</summary>
    public OtlpOptions Otlp { get; set; } = new();

    /// <summary>Distributed traces.</summary>
    public TracesOptions Traces { get; set; } = new();

    /// <summary>Metrics.</summary>
    public MetricsOptions Metrics { get; set; } = new();

    /// <summary>Logs.</summary>
    public LogsOptions Logs { get; set; } = new();

    /// <summary>
    /// What is wrong with these options, one message per problem (empty when valid): an endpoint that is not an
    /// absolute http(s) URL, an unknown protocol, a sampling ratio outside [0, 1], a non-positive timeout or export interval.
    /// Empty endpoints are valid (that signal is simply not exported).
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var failures = new List<string>();
        const string section = nameof(BaGetOptions.Telemetry);

        CheckEndpoint(failures, $"{section}:{nameof(Otlp)}:{nameof(OtlpOptions.Endpoint)}", Otlp?.Endpoint);
        CheckEndpoint(failures, $"{section}:{nameof(Metrics)}:{nameof(MetricsOptions.Endpoint)}", Metrics?.Endpoint);
        CheckEndpoint(failures, $"{section}:{nameof(Logs)}:{nameof(LogsOptions.Endpoint)}", Logs?.Endpoint);

        var protocols = AllowedSettingValuesAttribute.ValuesOf<OtlpOptions>(nameof(OtlpOptions.Protocol));
        if (Otlp != null && !protocols.Contains(Otlp.Protocol, StringComparer.OrdinalIgnoreCase))
        {
            failures.Add($"The '{section}:{nameof(Otlp)}:{nameof(OtlpOptions.Protocol)}' config is invalid. Allowed values: {string.Join(", ", protocols)}");
        }

        var levels = AllowedSettingValuesAttribute.ValuesOf<LogsOptions>(nameof(LogsOptions.MinimumLevel));
        if (Logs != null && !levels.Contains(Logs.MinimumLevel, StringComparer.OrdinalIgnoreCase))
        {
            failures.Add($"The '{section}:{nameof(Logs)}:{nameof(LogsOptions.MinimumLevel)}' config is invalid. Allowed values: {string.Join(", ", levels)}");
        }

        if (Traces != null && !(Traces.SamplingRatio >= 0 && Traces.SamplingRatio <= 1))
        {
            failures.Add($"The '{section}:{nameof(Traces)}:{nameof(TracesOptions.SamplingRatio)}' config must be between 0 and 1");
        }

        if (Otlp != null && Otlp.Timeout <= TimeSpan.Zero)
        {
            failures.Add($"The '{section}:{nameof(Otlp)}:{nameof(OtlpOptions.Timeout)}' config must be greater than zero");
        }

        if (Metrics != null && Metrics.ExportInterval <= TimeSpan.Zero)
        {
            failures.Add($"The '{section}:{nameof(Metrics)}:{nameof(MetricsOptions.ExportInterval)}' config must be greater than zero");
        }

        return failures;
    }

    private static void CheckEndpoint(List<string> failures, string key, string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return;

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            // The value is not echoed: a collector URL may carry a token.
            failures.Add($"The '{key}' config must be an absolute http or https URL");
        }
    }
}

/// <summary>The OTLP connection shared by all signals.</summary>
public class OtlpOptions
{
    /// <summary>
    /// The collector's absolute http(s) URL, for example a Jaeger or OpenTelemetry Collector.
    /// Empty means traces are not exported.
    /// </summary>
    [SettingExample(nameof(Protocol), "Grpc", "http://jaeger:4317")]
    [SettingExample(nameof(Protocol), "HttpProtobuf", "http://jaeger:4318")]
    public string? Endpoint { get; set; }

    /// <summary>The OTLP transport: gRPC (port 4317) or HTTP/protobuf (port 4318).</summary>
    [AllowedSettingValues("Grpc", "HttpProtobuf")]
    public string Protocol { get; set; } = "Grpc";

    /// <summary>Extra request headers as <c>key=value,key2=value2</c>, such as an API key for a hosted collector.</summary>
    [SensitiveSetting]
    public string? Headers { get; set; }

    /// <summary>How long an export may take before it is abandoned.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>Trace settings.</summary>
public class TracesOptions
{
    /// <summary>Export traces (when <see cref="TelemetryOptions.Enabled"/> and an endpoint are set).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The fraction of new traces recorded, from 0 (none) to 1 (all). A trace already sampled upstream is kept.</summary>
    public double SamplingRatio { get; set; } = 1.0;

    /// <summary>Record database commands as spans.</summary>
    public bool Database { get; set; } = true;
}

/// <summary>Metric settings.</summary>
public class MetricsOptions
{
    /// <summary>The collector to send metrics to. It does not fall back to <c>Otlp:Endpoint</c>: empty means metrics are not exported (Jaeger accepts traces only).</summary>
    public string? Endpoint { get; set; }

    /// <summary>How often metrics are exported.</summary>
    [RestartRequired]
    public TimeSpan ExportInterval { get; set; } = TimeSpan.FromSeconds(60);
}

/// <summary>Log export settings.</summary>
public class LogsOptions
{
    /// <summary>The collector to send logs to. It does not fall back to <c>Otlp:Endpoint</c>: empty means logs are not exported (Jaeger accepts traces only).</summary>
    public string? Endpoint { get; set; }

    /// <summary>The lowest Serilog level exported.</summary>
    [AllowedSettingValues("Verbose", "Debug", "Information", "Warning", "Error", "Fatal")]
    public string MinimumLevel { get; set; } = "Information";
}
