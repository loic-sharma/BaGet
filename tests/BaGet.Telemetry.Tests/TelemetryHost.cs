using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using BaGet.Core;
using BaGetApplication = BaGet.Tests.BaGetApplication;
using TestPackages = BaGet.Tests.TestPackages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Telemetry.Tests;

/// <summary>A real BaGet host (SQLite, temp directory) on which telemetry is switched on and off the way the Settings page does.</summary>
public sealed class TelemetryHost : IAsyncDisposable
{
    public const string ApiKey = "e2e-api-key";

    private int _counter;

    private TelemetryHost(BaGetApplication app, string serviceName)
    {
        App = app;
        ServiceName = serviceName;
        Client = app.CreateClient();
        Status = app.Services.GetRequiredService<TelemetryStatus>();
    }

    public BaGetApplication App { get; }

    public HttpClient Client { get; }

    public TelemetryStatus Status { get; }

    /// <summary>A name of its own, so a shared backend can tell this host's data from every other test's.</summary>
    public string ServiceName { get; }

    /// <summary>
    /// Telemetry stays off at startup: the harness sets no <c>Telemetry:Enabled</c>/endpoint key (a startup key would
    /// shadow the saved overrides), only the two that are read once at startup.
    /// </summary>
    public static TelemetryHost Start(ITestOutputHelper output, string metricsInterval = "00:00:01")
    {
        var serviceName = "e2e-" + Guid.NewGuid().ToString("N").Substring(0, 12);
        var app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            ["ApiKey"] = ApiKey,
            ["Telemetry:ServiceName"] = serviceName,
            ["Telemetry:Metrics:ExportInterval"] = metricsInterval,
        });
        return new TelemetryHost(app, serviceName);
    }

    /// <summary>Saves through the Settings page's service: rewrites the overrides file and reloads the configuration, no restart.</summary>
    public async Task SaveAsync(params (string Key, object Value)[] changes)
    {
        var service = App.Services.GetRequiredService<SettingsService>();
        var descriptors = changes.ToDictionary(c => service.Catalog.Find(c.Key)!, c => c.Value);
        var result = await service.SaveAsync(descriptors, "e2e", TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    public Task EnableAsync(string endpoint, string protocol = "Grpc", string? metrics = null, string? logs = null)
        => SaveAsync(
            ("Telemetry:Enabled", true),
            ("Telemetry:Otlp:Protocol", protocol),
            ("Telemetry:Otlp:Endpoint", endpoint),
            ("Telemetry:Otlp:Timeout", "00:00:03"),
            ("Telemetry:Metrics:Endpoint", metrics ?? ""),
            ("Telemetry:Logs:Endpoint", logs ?? ""));

    /// <summary>Pushes a new package with a unique id; returns its id and how long the request took.</summary>
    public async Task<(string Id, TimeSpan Elapsed)> PushAsync(string? prefix = null)
    {
        var id = $"{prefix ?? "E2E.Pkg"}.{ServiceName}.N{++_counter}";
        using var package = TestPackages.Build(id, "1.0.0");
        using var request = TestPackages.PushRequest(package, ApiKey);
        var watch = Stopwatch.StartNew();
        using var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);
        watch.Stop();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (id, watch.Elapsed);
    }

    public static async Task<T> PollAsync<T>(Func<Task<T?>> probe, TimeSpan timeout, string what) where T : class
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var value = await probe();
            if (value != null) return value;
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Not seen within {timeout.TotalSeconds:0} s: {what}");
            await Task.Delay(250, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>The traces Jaeger holds for a service, as raw JSON (null while none).</summary>
    public static async Task<string?> JaegerTracesAsync(Backend jaeger, string service)
    {
        var json = await Containers.Http.GetStringAsync($"{jaeger.QueryUrl}/api/traces?service={Uri.EscapeDataString(service)}&lookback=1h&limit=200", TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0 ? json : null;
    }

    /// <summary>Waits for Jaeger to hold a <c>package.index</c> span for <paramref name="packageId"/>; returns the wait.</summary>
    public async Task<TimeSpan> WaitForTraceAsync(Backend jaeger, string packageId, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        await PollAsync(async () =>
        {
            var json = await JaegerTracesAsync(jaeger, ServiceName);
            return json != null && json.Contains("package.index") && json.Contains(packageId) ? json : null;
        }, timeout, "trace of " + packageId);
        return watch.Elapsed;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.DisposeAsync();
    }
}
