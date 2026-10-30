using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Core.Tests.Support;
using BaGet.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// OpenTelemetry export end to end over the real pipeline (sampler, processors, reloadable exporters, Serilog sink),
/// with in-memory exporters in place of OTLP. The harness leaves telemetry off unless a test turns it on.
/// Joins the Diagnostics collection (runs alone): the "BaGet" source and the Entity Framework listener are process-wide,
/// so a host running in parallel would feed its spans to this host's provider.
/// </summary>
[Collection(DiagnosticsCapture.Name)]
public class TelemetryExportTests : IDisposable
{
    private const string ApiKey = "s3cr3t-api-key-value";
    private const string Collector = "http://collector.invalid:4317";

    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public TelemetryExportTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var disposable in _disposables) disposable.Dispose();
    }

    private static Dictionary<string, string> On(Dictionary<string, string>? extra = null)
    {
        var config = new Dictionary<string, string>
        {
            ["ApiKey"] = ApiKey,
            ["Telemetry:Enabled"] = "true",
            ["Telemetry:Otlp:Endpoint"] = Collector,
            ["Telemetry:Logs:Endpoint"] = Collector,
        };
        foreach (var pair in extra ?? new Dictionary<string, string>()) config[pair.Key] = pair.Value;
        return config;
    }

    private (BaGetApplication App, HttpClient Client, InMemoryTelemetry Telemetry) Start(Dictionary<string, string>? config = null, bool inMemory = true)
    {
        InMemoryTelemetry? telemetry = null;
        var all = config ?? new Dictionary<string, string> { ["ApiKey"] = ApiKey };
        var app = new BaGetApplication(
            _output,
            configOverrides: all,
            configureServices: inMemory ? services => telemetry = InMemoryTelemetry.Install(services) : null);
        var client = app.CreateClient();
        _disposables.Add(client);
        _disposables.Add(app);
        return (app, client, telemetry!);
    }

    private static async Task<HttpStatusCode> PushAsync(HttpClient client, string id, string suffix = "")
    {
        using var package = TestPackages.Build(id, "1.0.0");
        using var request = TestPackages.PushRequest(package, ApiKey);
        request.RequestUri = new Uri("api/v2/package" + suffix, UriKind.Relative);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    /// <summary>Saves through the Settings page's service, which rewrites the overrides file and reloads the configuration.</summary>
    private static async Task SaveAsync(BaGetApplication app, params (string Key, object Value)[] changes)
    {
        var service = app.Services.GetRequiredService<SettingsService>();
        var descriptors = changes.ToDictionary(c => service.Catalog.Find(c.Key)!, c => c.Value);
        var result = await service.SaveAsync(descriptors, "test", TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    private static Task EnableAsync(BaGetApplication app, string endpoint = Collector, string logsEndpoint = Collector)
        => SaveAsync(app, ("Telemetry:Enabled", true), ("Telemetry:Otlp:Endpoint", endpoint), ("Telemetry:Logs:Endpoint", logsEndpoint));

    /// <summary>Flushes until the spans satisfy <paramref name="ready"/>: the request span ends just after the response is sent.</summary>
    private static async Task<IReadOnlyList<ExportedSpan>> SpansWhenAsync(BaGetApplication app, InMemoryTelemetry telemetry, Func<IReadOnlyList<ExportedSpan>, bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            InMemoryTelemetry.Flush(app.Services);
            var spans = telemetry.Spans;
            if (ready(spans) || DateTime.UtcNow > deadline) return spans;
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The condition was not met.");
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ByDefaultNothingIsBuiltOrExported()
    {
        var (app, client, telemetry) = Start();

        Assert.Equal(HttpStatusCode.Created, await PushAsync(client, "Export.Off"));
        InMemoryTelemetry.Flush(app.Services);

        Assert.Empty(telemetry.Spans);
        Assert.Empty(telemetry.Logs);
        Assert.Equal(0, telemetry.TraceExportersCreated);
        Assert.Equal(0, telemetry.LogSinksCreated);
        Assert.All(app.Services.GetRequiredService<TelemetryStatus>().GetAll(), s => Assert.False(s.Active));
    }

    [Fact]
    public async Task APushIsOneTraceFromTheRequestThroughPackageIndexToTheDatabaseCommands()
    {
        var (app, client, telemetry) = Start(On());

        Assert.Equal(HttpStatusCode.Created, await PushAsync(client, "Export.Trace"));
        var spans = await SpansWhenAsync(app, telemetry, all => all.Any(s => s.Kind == ActivityKind.Server));

        var index = Assert.Single(spans, s => s.Name == "package.index");
        var trace = spans.Where(s => s.TraceId == index.TraceId).ToDictionary(s => s.SpanId);

        var request = Assert.Single(trace.Values, s => s.Kind == ActivityKind.Server);
        var databaseSpans = trace.Values.Where(s => s.Source.Contains("EntityFrameworkCore")).ToArray();
        Assert.NotEmpty(databaseSpans);

        Assert.True(IsAncestor(trace, request, index), "package.index is not under the request span");
        Assert.All(databaseSpans, s => Assert.True(IsAncestor(trace, request, s), "a database span is outside the request"));
        Assert.Contains(databaseSpans, s => IsAncestor(trace, index, s));
        Assert.Equal("Export.Trace", index.Tags[BaGetDiagnostics.Tags.PackageId]);
    }

    [Fact]
    public async Task DatabaseSpansFollowTheLiveDatabaseSwitch()
    {
        var (app, client, telemetry) = Start(On(new Dictionary<string, string> { ["Telemetry:Traces:Database"] = "false" }));

        Assert.Equal(HttpStatusCode.Created, await PushAsync(client, "Export.NoDb"));
        InMemoryTelemetry.Flush(app.Services);

        Assert.Contains(telemetry.Spans, s => s.Name == "package.index");
        Assert.DoesNotContain(telemetry.Spans, s => s.Source.Contains("EntityFrameworkCore"));
    }

    [Fact]
    public async Task EnablingAndDisablingOnTheSettingsPageStartsAndStopsSpansWithoutARestart()
    {
        // Off from the start (the harness sets no Telemetry key, so the saved overrides are not shadowed).
        var (app, client, telemetry) = Start();
        var status = app.Services.GetRequiredService<TelemetryStatus>();
        Assert.Equal(HttpStatusCode.Created, await PushAsync(client, "Export.Live0"));
        InMemoryTelemetry.Flush(app.Services);
        Assert.Empty(telemetry.Spans);

        await EnableAsync(app);
        await WaitForAsync(() => status.Get(TelemetrySignal.Traces).Active);
        Assert.Equal(HttpStatusCode.Created, await PushAsync(client, "Export.Live1"));
        var before = (await SpansWhenAsync(app, telemetry, all => all.Any(s => s.Kind == ActivityKind.Server))).Count;
        Assert.True(before > 0);

        await SaveAsync(app, ("Telemetry:Enabled", false));
        await WaitForAsync(() => !status.Get(TelemetrySignal.Traces).Active);
        Assert.Equal(HttpStatusCode.Created, await PushAsync(client, "Export.Live2"));
        await Task.Delay(300, TestContext.Current.CancellationToken);
        InMemoryTelemetry.Flush(app.Services);
        Assert.Equal(before, telemetry.Spans.Count);

        await SaveAsync(app, ("Telemetry:Enabled", true));
        await WaitForAsync(() => status.Get(TelemetrySignal.Traces).Active);
        Assert.Equal(HttpStatusCode.Created, await PushAsync(client, "Export.Live3"));
        var after = await SpansWhenAsync(app, telemetry, all => all.Any(s => s.Name == "package.index" && s.Tags[BaGetDiagnostics.Tags.PackageId] == "Export.Live3"));

        Assert.Contains(after, s => s.Name == "package.index" && s.Tags[BaGetDiagnostics.Tags.PackageId] == "Export.Live3");
        Assert.DoesNotContain(after, s => s.Tags.Values.Contains("Export.Live2"));
        Assert.Equal(2, telemetry.TraceExportersCreated);
    }

    [Fact]
    public async Task ALogEmittedInsideARequestCarriesTheRequestsTraceId()
    {
        var (app, client, telemetry) = Start(On());

        Assert.Equal(HttpStatusCode.Created, await PushAsync(client, "Export.Logs"));
        var spans = await SpansWhenAsync(app, telemetry, all => all.Any(s => s.Kind == ActivityKind.Server));

        var request = Assert.Single(spans, s => s.Kind == ActivityKind.Server && s.Name.Contains("PUT"));
        var line = Assert.Single(telemetry.Logs, e => e.MessageTemplate.Text.StartsWith("HTTP {RequestMethod}") && e.Properties["RequestMethod"].ToString().Contains("PUT"));
        Assert.Equal(request.TraceId, line.TraceId?.ToHexString());
        Assert.Equal(request.SpanId, line.SpanId?.ToHexString());

        // The Hosting.Diagnostics floor still holds: no event with the request URL (and its query) is exported.
        Assert.DoesNotContain(telemetry.Logs, e => e.Properties.TryGetValue("SourceContext", out var c) && c.ToString().Contains("Hosting.Diagnostics"));
    }

    [Fact]
    public async Task LogsAreNotExportedWithoutALogsEndpointOnlyTraces()
    {
        var (app, client, telemetry) = Start(On(new Dictionary<string, string> { ["Telemetry:Logs:Endpoint"] = "" }));

        Assert.Equal(HttpStatusCode.Created, await PushAsync(client, "Export.NoLogs"));
        InMemoryTelemetry.Flush(app.Services);

        Assert.NotEmpty(telemetry.Spans);
        Assert.Empty(telemetry.Logs);
        Assert.Equal(0, telemetry.LogSinksCreated);
    }

    [Fact]
    public async Task NeitherQueryValuesNorHeadersReachSpansLogsOrStatus()
    {
        var (app, client, telemetry) = Start(On(new Dictionary<string, string> { ["Telemetry:Otlp:Headers"] = "api-key=OTLP-HEADER-SECRET" }));

        Assert.Equal(HttpStatusCode.Created, await PushAsync(client, "Export.Secrets", "?apikey=QUERY-SECRET-VALUE&token=QUERY-SECRET-2"));
        using (var response = await client.GetAsync("v3/index.json?apikey=QUERY-SECRET-VALUE", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var spans = await SpansWhenAsync(app, telemetry, all => all.Count(s => s.Kind == ActivityKind.Server) >= 2);
        Assert.NotEmpty(spans);
        var everything = spans.SelectMany(s => s.Tags.Select(t => t.Key + "=" + t.Value))
            .Concat(telemetry.Logs.Select(l => l.RenderMessage() + string.Join(";", l.Properties.Select(p => p.Key + "=" + p.Value))))
            .Concat(app.Services.GetRequiredService<TelemetryStatus>().GetAll().Select(s => s.LastError ?? ""))
            .ToArray();

        foreach (var secret in new[] { "QUERY-SECRET-VALUE", "QUERY-SECRET-2", ApiKey, "OTLP-HEADER-SECRET" })
        {
            Assert.DoesNotContain(everything, text => text.Contains(secret));
        }

        Assert.DoesNotContain(spans.SelectMany(s => s.Tags.Keys), key => key.StartsWith("http.request.header", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(spans.SelectMany(s => s.Tags), t => t.Key == "url.query" && t.Value!.Contains("Redacted"));
    }

    [Fact]
    public async Task DatabaseSpansNeverCarrySqlText()
    {
        var (app, client, telemetry) = Start(On());

        Assert.Equal(HttpStatusCode.Created, await PushAsync(client, "Export.NoSql"));
        var spans = await SpansWhenAsync(app, telemetry, all => all.Any(s => s.Kind == ActivityKind.Server));

        var databaseSpans = spans.Where(s => s.Source.Contains("EntityFrameworkCore")).ToArray();
        Assert.NotEmpty(databaseSpans);
        Assert.DoesNotContain(databaseSpans.SelectMany(s => s.Tags.Keys), key => key is "db.query.text" or "db.statement");
        Assert.DoesNotContain(databaseSpans, s => s.Tags.Values.Any(v => v != null && v.Contains("SELECT", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task AnUnreachableEndpointNeverSlowsRequestsAndTheStatusShowsFailingWithoutTheHeaders()
    {
        var (app, client, _) = Start(inMemory: false);
        await SaveAsync(
            app,
            ("Telemetry:Enabled", true),
            ("Telemetry:Otlp:Endpoint", "http://127.0.0.1:1"),
            ("Telemetry:Otlp:Timeout", "00:00:02"),
            ("Telemetry:Otlp:Headers", "api-key=OTLP-HEADER-SECRET"));

        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Created, await PushAsync(client, "Export.Down" + i));
        }

        // The requests only enqueue spans: they take as long as without telemetry, not as long as the export timeout.
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), "requests were slowed by the export: " + watch.Elapsed);

        var status = app.Services.GetRequiredService<TelemetryStatus>();
        app.Services.GetRequiredService<TracerProvider>().ForceFlush(15_000);
        await WaitForAsync(() => status.Get(TelemetrySignal.Traces).FailingSince != null);

        var traces = status.Get(TelemetrySignal.Traces);
        Assert.True(traces.Active);
        Assert.Null(traces.LastSuccess);
        Assert.NotNull(traces.LastError);
        Assert.DoesNotContain("OTLP-HEADER-SECRET", traces.LastError);

        // Switching off stops the attempts: the failure history is reset.
        await SaveAsync(app, ("Telemetry:Enabled", false));
        await WaitForAsync(() => !status.Get(TelemetrySignal.Traces).Active);
        Assert.Null(status.Get(TelemetrySignal.Traces).FailingSince);
    }

    [Fact]
    public async Task TheRealOtlpExportersPostTracesMetricsAndLogsWithTheHeadersPathsAndServiceName()
    {
        await using var collector = await FakeOtlpCollector.StartAsync();
        var (app, client, _) = Start(
            new Dictionary<string, string>
            {
                ["ApiKey"] = ApiKey,
                ["Telemetry:ServiceName"] = "baget-under-test",
                ["Telemetry:Metrics:ExportInterval"] = "00:00:01",
            },
            inMemory: false);
        await SaveAsync(
            app,
            ("Telemetry:Enabled", true),
            ("Telemetry:Otlp:Endpoint", collector.Url),
            ("Telemetry:Otlp:Protocol", "HttpProtobuf"),
            ("Telemetry:Otlp:Headers", "x-test=header-value"),
            ("Telemetry:Metrics:Endpoint", collector.Url),
            ("Telemetry:Logs:Endpoint", collector.Url));

        Assert.Equal(HttpStatusCode.Created, await PushAsync(client, "Export.Real"));

        var deadline = DateTime.UtcNow.AddSeconds(30);
        CollectorRequest? Find(string path, string text) => collector.Requests.FirstOrDefault(r => r.Path == path && r.Text.Contains(text));
        while ((Find("/v1/traces", "package.index") == null || Find("/v1/metrics", "baget.packages.pushed") == null || Find("/v1/logs", "Successfully indexed") == null)
            && DateTime.UtcNow < deadline)
        {
            app.Services.GetRequiredService<TracerProvider>().ForceFlush(5_000);
            app.Services.GetRequiredService<MeterProvider>().ForceFlush(5_000);
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        var traces = Find("/v1/traces", "package.index");
        Assert.NotNull(traces);
        Assert.Contains("baget-under-test", traces!.Text);
        Assert.NotNull(Find("/v1/metrics", "baget.packages.pushed"));
        var logs = Find("/v1/logs", "Successfully indexed");
        Assert.NotNull(logs);
        Assert.Contains("baget-under-test", logs!.Text);
        Assert.All(collector.Requests, r => Assert.Equal("header-value", r.XTest));

        var status = app.Services.GetRequiredService<TelemetryStatus>();
        Assert.All(status.GetAll(), s => Assert.Null(s.FailingSince));
        Assert.NotNull(status.Get(TelemetrySignal.Traces).LastSuccess);
        await WaitForAsync(() => status.Get(TelemetrySignal.Logs).LastSuccess != null);
    }

    [Fact]
    public async Task TestConnectionReportsOkForAListeningEndpointAndAScrubbedErrorOtherwise()
    {
        var (app, _, _) = Start();
        var tester = app.Services.GetRequiredService<TelemetryConnectionTester>();

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var ok = await tester.TestAsync("http://127.0.0.1:" + port, "Grpc", "api-key=OTLP-HEADER-SECRET", TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(ok.Ok, ok.Message);
        }
        finally
        {
            listener.Stop();
        }

        var refused = await tester.TestAsync("http://127.0.0.1:" + port, "Grpc", "api-key=OTLP-HEADER-SECRET", TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(refused.Ok);
        Assert.DoesNotContain("OTLP-HEADER-SECRET", refused.Message);

        var invalid = await tester.TestAsync("not a url", "Grpc", null, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(invalid.Ok);
        Assert.Contains("http", invalid.Message);
    }

    [Fact]
    public async Task TestConnectionExportsASyntheticSpanThroughTheConfiguredExporter()
    {
        var (app, _, telemetry) = Start();
        var tester = app.Services.GetRequiredService<TelemetryConnectionTester>();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var result = await tester.TestAsync("http://127.0.0.1:" + port, "Grpc", null, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.True(result.Ok, result.Message);
            Assert.Equal(1, telemetry.TraceExportersCreated);
            Assert.Contains(telemetry.Spans, s => s.Name == "baget.telemetry.test");
        }
        finally
        {
            listener.Stop();
        }

        // Nothing was saved or enabled by testing.
        Assert.False(app.Services.GetRequiredService<TelemetryStatus>().Get(TelemetrySignal.Traces).Active);
    }

    [Theory]
    [InlineData("failed http://user:pw@host:4317/v1/traces?token=abc now", "failed http://host:4317 now")]
    [InlineData("rejected key=SUPER-SECRET here", "rejected key=*** here")]
    public void ScrubKeepsOnlyTheHostOfUrlsAndHidesSecrets(string message, string expected)
        => Assert.Equal(expected, TelemetryStatus.Scrub(message, "x-key=SUPER-SECRET"));

    private static bool IsAncestor(IReadOnlyDictionary<string, ExportedSpan> trace, ExportedSpan ancestor, ExportedSpan span)
    {
        var current = span;
        for (var i = 0; i < 100 && current.ParentSpanId != null; i++)
        {
            if (current.ParentSpanId == ancestor.SpanId) return true;
            if (!trace.TryGetValue(current.ParentSpanId, out current!)) return false;
        }

        return false;
    }
}
