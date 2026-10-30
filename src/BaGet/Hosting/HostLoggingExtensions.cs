using System;
using System.Collections.Generic;
using System.IO;
using BaGet.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace BaGet;

/// <summary>Serilog, the host's only logging stack.</summary>
internal static class HostLoggingExtensions
{
    /// <summary>
    /// Replace the default Microsoft logging providers with Serilog, configured entirely from the
    /// "Serilog" configuration section (levels, sinks, retention). Libraries keep logging through
    /// <see cref="Microsoft.Extensions.Logging.ILogger"/>; only the host knows about Serilog.
    /// </summary>
    /// <remarks>
    /// There is deliberately no static <c>Log.Logger</c> or bootstrap logger (<c>preserveStaticLogger</c>; the default would assign it per host): WebApplicationFactory builds
    /// several hosts in one process, and a frozen static logger would throw on the second. Each host gets
    /// its own logger, disposed with it. <c>writeToProviders</c> keeps any <c>ILoggerProvider</c> a
    /// host registers explicitly (the tests' capturing providers) fed as well.
    /// OpenTelemetry log export is one more Serilog sink (<see cref="ReloadableOpenTelemetrySink"/>), not an <c>ILoggerProvider</c>, which
    /// <c>writeToProviders</c> would feed twice.
    /// </remarks>
    public static WebApplicationBuilder AddBaGetLogging(this WebApplicationBuilder builder)
    {
        var root = Environment.GetEnvironmentVariable("BAGET_CONFIG_ROOT");
        var configuration = builder.Configuration;
        configuration.ResolveLogFilePaths(string.IsNullOrEmpty(root) ? builder.Environment.ContentRootPath : root);
        configuration.DisableSinks();

        // A failing sink (an unwritable logs folder under IIS) is otherwise silent: Serilog swallows sink errors.
        Serilog.Debugging.SelfLog.Enable(message => Console.Error.WriteLine(message));

        // Replaces the Microsoft providers, the Windows Event Log one included: see AddEventLogSink.
        builder.Logging.ClearProviders();
        builder.Services.AddSerilog(
            (services, loggerConfiguration) =>
            {
                loggerConfiguration.ApplyDefaultMinimumLevels();
                loggerConfiguration
                    .ReadFrom.Configuration(configuration)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext()
                    .Enrich.With<RequestPathRedactingEnricher>();
                loggerConfiguration.AddEventLogSink(configuration);
                // A Serilog sink, not an ILoggerProvider: writeToProviders would deliver each event to a provider twice.
                // Idle (returns at once) until Telemetry:Logs:Endpoint is set; carries TraceId/SpanId.
                loggerConfiguration.WriteTo.Sink(services.GetRequiredService<ReloadableOpenTelemetrySink>());
            },
            preserveStaticLogger: true,
            writeToProviders: true);

        return builder;
    }

    /// <summary>
    /// The levels in force when the "Serilog" section sets none, so that a host with its own appsettings.json
    /// (one that embeds <see cref="Program.Main"/>) is still safe. They are applied before
    /// <c>ReadFrom.Configuration</c>: a configured <c>Default</c> replaces the default here and a configured
    /// <c>Override</c> replaces the override for the same namespace, but an override the configuration does not
    /// mention survives. <c>Microsoft.AspNetCore.Hosting.Diagnostics</c> at Information logs every full request
    /// URL, query string (<c>?apikey=</c>) included, hence the Warning floor; lowering it in configuration is
    /// the operator's explicit choice.
    /// </summary>
    public static LoggerConfiguration ApplyDefaultMinimumLevels(this LoggerConfiguration loggerConfiguration)
    {
        loggerConfiguration.MinimumLevel.Information();
        loggerConfiguration.MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning);
        loggerConfiguration.MinimumLevel.Override("Microsoft.Hosting.Lifetime", Serilog.Events.LogEventLevel.Information);
        loggerConfiguration.MinimumLevel.Override("Microsoft.AspNetCore.Hosting.Diagnostics", Serilog.Events.LogEventLevel.Warning);
        return loggerConfiguration;
    }

    /// <summary>
    /// <c>WebApplication.CreateBuilder</c> adds the Microsoft Event Log provider on Windows (Warning and up,
    /// the place startup errors reach under IIS in-process or as a service); <c>ClearProviders</c> removes it.
    /// This Serilog sink restores that: Warning and up to the Application log. Unlike a Microsoft provider it
    /// is not fed twice by <c>writeToProviders</c>. The default source, ".NET Runtime", exists on every
    /// Windows, so (as before) no administrator right is needed to register one; a custom
    /// "Serilog:EventLog:Source" must already be registered. "Serilog:EventLog:Enabled=false" turns it off.
    /// </summary>
    private static void AddEventLogSink(this LoggerConfiguration loggerConfiguration, IConfiguration configuration)
    {
        if (!OperatingSystem.IsWindows() || !IsEnabled(configuration.GetSection("Serilog:EventLog")["Enabled"]))
        {
            return;
        }

        var source = configuration["Serilog:EventLog:Source"];
        loggerConfiguration.WriteTo.EventLog(
            source: string.IsNullOrWhiteSpace(source) ? ".NET Runtime" : source,
            logName: "Application",
            manageEventSource: false,
            restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning);
    }

    private static bool IsEnabled(string? value) => !bool.TryParse(value, out var enabled) || enabled;

    /// <summary>
    /// Make the relative <c>path</c> of every File sink under "Serilog:WriteTo" absolute against the content
    /// root (or BAGET_CONFIG_ROOT). A relative path would otherwise resolve against the process working
    /// directory, which for a service is System32 or wherever it was launched from. Sinks are found by
    /// <c>Name</c> ("File", case-insensitive), whether keyed by name (<c>WriteTo:File</c>) or by array index
    /// (<c>WriteTo:0</c>). An absolute path, from any configuration source, is left alone.
    /// </summary>
    public static void ResolveLogFilePaths(this IConfigurationManager configuration, string root)
    {
        var resolved = new Dictionary<string, string?>();
        foreach (var sink in configuration.GetSection("Serilog:WriteTo").GetChildren())
        {
            if (!string.Equals(sink["Name"], "File", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = sink.Path + ":Args:path";
            var path = configuration[key];

            if (!string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path))
            {
                resolved[key] = Path.GetFullPath(Path.Combine(root, path));
            }
        }

        AddLast(configuration, resolved);
    }

    /// <summary>
    /// Adds <paramref name="values"/> as a source of their own. Setting them through the indexer would write them into the
    /// providers that exist now, and a source added later (a test harness, an embedding host) rebuilds every provider and
    /// drops them.
    /// </summary>
    private static void AddLast(IConfigurationManager configuration, Dictionary<string, string?> values)
    {
        if (values.Count > 0) configuration.AddInMemoryCollection(values);
    }

    /// <summary>
    /// A sink with <c>Enabled=false</c> is switched off (<c>Serilog__WriteTo__File__Enabled=false</c>). An empty
    /// <c>Name</c> would too, but an empty environment variable is a deleted one on Windows, so it is no
    /// portable switch. Works for sinks keyed by name or by array index.
    /// </summary>
    public static void DisableSinks(this IConfigurationManager configuration)
    {
        var disabled = new Dictionary<string, string?>();
        foreach (var sink in configuration.GetSection("Serilog:WriteTo").GetChildren())
        {
            if (!IsEnabled(sink["Enabled"]))
            {
                // Serilog.Settings.Configuration skips a sink whose Name is empty.
                disabled[sink.Path + ":Name"] = "";
            }
        }

        AddLast(configuration, disabled);
    }
}
