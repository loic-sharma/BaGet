using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using BaGet.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Instrumentation.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BaGet.Telemetry;

/// <summary>Registers BaGet's OpenTelemetry export in the host. The libraries only emit <see cref="BaGetDiagnostics"/>.</summary>
internal static class TelemetryServiceCollectionExtensions
{
    /// <summary>The span queue is bounded: a collector that is down costs at most this many spans, then spans are dropped.</summary>
    internal const int MaxTraceQueueSize = 2048;

    /// <summary>How long a batch waits before it is exported; with the defaults a trace shows in the backend within seconds.</summary>
    internal const int TraceBatchDelayMilliseconds = 2000;

    private static readonly string[] StaticExtensions =
    {
        ".css", ".js", ".map", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico", ".webp", ".woff", ".woff2", ".ttf", ".eot", ".txt", ".webmanifest",
    };

    /// <summary>
    /// Providers are always built; whether anything is exported is decided live by <see cref="DynamicSampler"/> and the
    /// <see cref="ReloadableExporter{T}"/>s from <c>Telemetry:*</c>, which the Settings page changes without a restart.
    /// Only <c>Telemetry:ServiceName</c> and <c>Telemetry:Metrics:ExportInterval</c> are read once, at startup.
    /// </summary>
    public static IServiceCollection AddBaGetTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(nameof(BaGetOptions.Telemetry));

        // The options are bound by AddBaGetOptions, which has no change token: without this a reload never reaches
        // IOptionsMonitor<TelemetryOptions> and the exporters would never be rebuilt.
        services.AddSingleton<IOptionsChangeTokenSource<TelemetryOptions>>(
            new ConfigurationChangeTokenSource<TelemetryOptions>(Options.DefaultName, section));

        services.TryAddSingleton<TelemetryStatus>();
        services.TryAddSingleton<ITelemetryExporterFactory, OtlpExporterFactory>();
        services.TryAddSingleton<ReloadableTraceExporter>();
        services.TryAddSingleton<ReloadableMetricExporter>();
        services.TryAddSingleton<ReloadableOpenTelemetrySink>();
        services.TryAddSingleton(provider => new TelemetryConnectionTester(provider.GetRequiredService<ITelemetryExporterFactory>()));
        services.TryAddSingleton<ITelemetryDiagnostics, TelemetryDiagnostics>();

        var startup = section.Get<TelemetryOptions>() ?? new TelemetryOptions();
        var serviceName = TelemetryEndpoints.ServiceNameOf(startup);
        var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(Program).Assembly.GetName().Version?.ToString();
        var interval = startup.Metrics?.ExportInterval > TimeSpan.Zero ? startup.Metrics!.ExportInterval : TimeSpan.FromSeconds(60);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName, serviceVersion: version))
            .WithTracing(tracing => tracing
                .SetSampler(provider => new DynamicSampler(provider.GetRequiredService<IOptionsMonitor<TelemetryOptions>>()))
                .AddSource(BaGetDiagnostics.Name)
                // Query values are redacted by the instrumentation (url.query becomes "key=Redacted") and request headers
                // are never recorded: do not add an Enrich that copies either (X-NuGet-ApiKey, ?apikey=).
                .AddAspNetCoreInstrumentation(options => options.Filter = IsNotStaticFile)
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                .ConfigureServices(inner => inner.AddOptions<EntityFrameworkInstrumentationOptions>().Configure<IOptionsMonitor<TelemetryOptions>>(
                    (options, monitor) =>
                    {
                        // Live: Telemetry:Traces:Database. The instrumentation always records the SQL text; SqlTextRedactingProcessor removes it.
                        options.Filter = (_, _) => TelemetryEndpoints.Read(monitor)?.Traces?.Database != false;
                    }))
                .AddProcessor<SqlTextRedactingProcessor>()
                // npm logout names the API key to revoke in the path (url.path): mask it before the batch is queued.
                .AddProcessor<PathRedactingProcessor>()
                .AddProcessor(provider =>
                {
                    var exporter = provider.GetRequiredService<ReloadableTraceExporter>();
                    var timeout = (int)Math.Min(
                        int.MaxValue,
                        (TelemetryEndpoints.Read(provider.GetRequiredService<IOptionsMonitor<TelemetryOptions>>())?.Otlp?.Timeout
                            ?? TimeSpan.FromSeconds(10)).TotalMilliseconds);
                    var processor = new BatchActivityExportProcessor(
                        exporter,
                        maxQueueSize: MaxTraceQueueSize,
                        scheduledDelayMilliseconds: TraceBatchDelayMilliseconds,
                        exporterTimeoutMilliseconds: timeout,
                        maxExportBatchSize: 512);
                    provider.GetRequiredService<TelemetryStatus>().Traces.Configured(
                        TelemetryEndpoints.Read(provider.GetRequiredService<IOptionsMonitor<TelemetryOptions>>()) is { } current
                            && TelemetryEndpoints.Resolve(current, TelemetrySignal.Traces) != null,
                        () => DroppedCount(processor));
                    return processor;
                }))
            .WithMetrics(metrics => metrics
                .AddMeter(BaGetDiagnostics.Name)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddReader(provider => new PeriodicExportingMetricReader(
                    provider.GetRequiredService<ReloadableMetricExporter>(),
                    exportIntervalMilliseconds: (int)interval.TotalMilliseconds,
                    exportTimeoutMilliseconds: 30_000)));

        return services;
    }

    private static readonly PropertyInfo? Dropped = typeof(BatchActivityExportProcessor).BaseType?.GetProperty("DroppedCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    /// <summary>Spans dropped because the queue was full; the SDK keeps the count internal, so 0 if it cannot be read.</summary>
    private static long DroppedCount(BaseProcessor<Activity> processor) => Dropped?.GetValue(processor) is long count ? count : 0;

    /// <summary>Static assets are not worth a span each; everything else (API, UI pages) is traced.</summary>
    internal static bool IsNotStaticFile(HttpContext context)
    {
        var path = context.Request.Path.Value;
        if (string.IsNullOrEmpty(path)) return true;

        if (path!.StartsWith("/_content/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/_framework/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return !StaticExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
}
