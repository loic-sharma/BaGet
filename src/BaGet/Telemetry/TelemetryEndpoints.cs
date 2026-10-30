using System;
using System.Collections.Generic;
using BaGet.Core;
using Microsoft.Extensions.Options;

namespace BaGet.Telemetry;

/// <summary>Turns <see cref="TelemetryOptions"/> into the endpoint a signal is exported to, or null when it is off.</summary>
internal static class TelemetryEndpoints
{
    /// <summary>The current options, or null when they cannot be read (invalid values count as off).</summary>
    public static TelemetryOptions? Read(IOptionsMonitor<TelemetryOptions> options)
    {
        try
        {
            return options.CurrentValue;
        }
        catch (OptionsValidationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Where <paramref name="signal"/> goes, or null while it is not exported: telemetry disabled, the signal disabled,
    /// no endpoint, or an endpoint that is not an absolute http(s) URL. Traces use <c>Otlp:Endpoint</c>; metrics and
    /// logs use only their own endpoint (Jaeger accepts traces only, so they are never silently pointed at it).
    /// With <c>HttpProtobuf</c> the signal's path (<c>/v1/traces</c>...) is appended unless already there.
    /// </summary>
    public static Uri? Resolve(TelemetryOptions options, TelemetrySignal signal)
    {
        if (!options.Enabled) return null;

        string? endpoint;
        string path;
        switch (signal)
        {
            case TelemetrySignal.Traces:
                if (options.Traces?.Enabled == false) return null;
                endpoint = options.Otlp?.Endpoint;
                path = "v1/traces";
                break;
            case TelemetrySignal.Metrics:
                endpoint = options.Metrics?.Endpoint;
                path = "v1/metrics";
                break;
            default:
                endpoint = options.Logs?.Endpoint;
                path = "v1/logs";
                break;
        }

        if (string.IsNullOrWhiteSpace(endpoint)
            || !Uri.TryCreate(endpoint!.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        if (!IsGrpc(options) && !uri.AbsolutePath.TrimEnd('/').EndsWith(path, StringComparison.OrdinalIgnoreCase))
        {
            uri = new Uri(uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/" + path + uri.Query);
        }

        return uri;
    }

    public static bool IsGrpc(TelemetryOptions options)
        => !string.Equals(options.Otlp?.Protocol, "HttpProtobuf", StringComparison.OrdinalIgnoreCase);

    /// <summary>The headers as a dictionary; entries are <c>key=value</c> separated by commas, values may contain '='.</summary>
    public static Dictionary<string, string> ParseHeaders(string? headers)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(headers)) return result;

        foreach (var pair in headers!.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var index = pair.IndexOf('=');
            if (index <= 0) continue;

            result[pair.Substring(0, index).Trim()] = pair.Substring(index + 1).Trim();
        }

        return result;
    }

    /// <summary>
    /// The <c>service.name</c> for a set of options: the configured name, else <c>baget</c>. <c>Telemetry:ServiceName</c> is
    /// read once at startup, so every signal (including the log sink, which is rebuilt on other changes) must use the name
    /// read then; it is deliberately not part of <see cref="Key"/>.
    /// </summary>
    public static string ServiceNameOf(TelemetryOptions? options)
        => string.IsNullOrWhiteSpace(options?.ServiceName) ? "baget" : options!.ServiceName;

    /// <summary>Everything that decides how a signal's exporter is built (not <c>ServiceName</c>, fixed at startup); equal keys mean no rebuild is needed. Null = off.</summary>
    public static string? Key(TelemetryOptions options, TelemetrySignal signal)
    {
        var endpoint = Resolve(options, signal);
        if (endpoint == null) return null;

        var level = signal == TelemetrySignal.Logs ? options.Logs?.MinimumLevel : null;
        return string.Join("|", endpoint, options.Otlp?.Protocol, options.Otlp?.Headers, options.Otlp?.Timeout, level);
    }
}
