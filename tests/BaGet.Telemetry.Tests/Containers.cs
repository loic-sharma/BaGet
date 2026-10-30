using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace BaGet.Telemetry.Tests;

/// <summary>A started telemetry backend: where to send OTLP, and how to read back what it received.</summary>
public sealed class Backend
{
    private readonly IContainer _container;

    internal Backend(IContainer container, int grpcPort, int httpPort, int? queryPort)
    {
        _container = container;
        GrpcUrl = $"http://{container.Hostname}:{container.GetMappedPublicPort(grpcPort)}";
        HttpUrl = $"http://{container.Hostname}:{container.GetMappedPublicPort(httpPort)}";
        QueryUrl = queryPort == null ? null : $"http://{container.Hostname}:{container.GetMappedPublicPort(queryPort.Value)}";
    }

    /// <summary>OTLP over gRPC (container port 4317).</summary>
    public string GrpcUrl { get; }

    /// <summary>OTLP over HTTP/protobuf (container port 4318).</summary>
    public string HttpUrl { get; }

    /// <summary>Jaeger's query API (16686); null for the collector.</summary>
    public string? QueryUrl { get; }

    /// <summary>The container's stdout and stderr so far (the collector's debug exporter prints what it received there).</summary>
    public async Task<string> LogsAsync()
    {
        var (stdout, stderr) = await _container.GetLogsAsync().ConfigureAwait(false);
        return stdout + stderr;
    }
}

/// <summary>
/// Starts Jaeger and an OpenTelemetry Collector on first use and shares them for the whole run. When Docker is not
/// available the tests skip instead of failing, so the normal suite stays green without it (as in BaGet.Database.Tests).
/// </summary>
public static class Containers
{
    // Jaeger v1's last line: OTLP is on by default (4317 gRPC, 4318 HTTP), the query API is on 16686.
    private const string JaegerImage = "jaegertracing/all-in-one:1.76.0";
    private const string CollectorImage = "otel/opentelemetry-collector:0.140.0";

    private const string CollectorConfig = """
        receivers:
          otlp:
            protocols:
              grpc:
                endpoint: 0.0.0.0:4317
              http:
                endpoint: 0.0.0.0:4318
        exporters:
          debug:
            verbosity: detailed
        service:
          pipelines:
            traces:
              receivers: [otlp]
              exporters: [debug]
            metrics:
              receivers: [otlp]
              exporters: [debug]
            logs:
              receivers: [otlp]
              exporters: [debug]
        """;

    private static readonly Lazy<Task<Backend>> JaegerStart = new(StartJaegerAsync);
    private static readonly Lazy<Task<Backend>> CollectorStart = new(StartCollectorAsync);

    public static HttpClient Http { get; } = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static Task<Backend> JaegerAsync() => GetOrSkipAsync(JaegerStart, "Jaeger");

    public static Task<Backend> CollectorAsync() => GetOrSkipAsync(CollectorStart, "The OpenTelemetry Collector");

    private static async Task<Backend> GetOrSkipAsync(Lazy<Task<Backend>> start, string what)
    {
        try
        {
            return await start.Value.ConfigureAwait(false);
        }
        catch (Exception e) when (IsDockerUnavailable(e))
        {
            Assert.Skip($"{what} needs Docker, which is not available: {e.GetBaseException().Message}");
            throw;
        }
    }

    private static bool IsDockerUnavailable(Exception e)
    {
        for (var inner = e; inner != null; inner = inner.InnerException)
        {
            if ((inner.GetType().FullName ?? string.Empty).Contains("Docker", StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static async Task<Backend> StartJaegerAsync()
    {
        var container = new ContainerBuilder(JaegerImage)
            .WithPortBinding(4317, true)
            .WithPortBinding(4318, true)
            .WithPortBinding(16686, true)
            .WithEnvironment("COLLECTOR_OTLP_ENABLED", "true")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(16686).ForPath("/")))
            .Build();
        await container.StartAsync().ConfigureAwait(false);
        return new Backend(container, 4317, 4318, 16686);
    }

    private static async Task<Backend> StartCollectorAsync()
    {
        var container = new ContainerBuilder(CollectorImage)
            .WithPortBinding(4317, true)
            .WithPortBinding(4318, true)
            .WithResourceMapping(Encoding.UTF8.GetBytes(CollectorConfig), "/etc/otelcol/config.yaml")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Everything is ready"))
            .Build();
        await container.StartAsync().ConfigureAwait(false);
        return new Backend(container, 4317, 4318, null);
    }
}
