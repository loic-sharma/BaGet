using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Core.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// The "BaGet" spans and counters over the real pipeline, the Telemetry section in the Settings catalog, and the
/// startup validation. Joins the Diagnostics collection so counters can be counted exactly.
/// </summary>
[Collection(DiagnosticsCapture.Name)]
public class TelemetryTests : IDisposable
{
    private const string ApiKey = "s3cr3t-api-key-value";

    private readonly ITestOutputHelper _output;
    private readonly DiagnosticsCapture _capture = new();
    private readonly List<IDisposable> _disposables = new();

    public TelemetryTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        _capture.Dispose();
        foreach (var disposable in _disposables) disposable.Dispose();
    }

    private (BaGetApplication App, HttpClient Client) Start(Dictionary<string, string>? config = null)
    {
        var app = new BaGetApplication(_output, configOverrides: config);
        var client = app.CreateClient();
        _disposables.Add(client);
        _disposables.Add(app);
        return (app, client);
    }

    private static async Task<HttpStatusCode> PushAsync(HttpClient client, string id, string version, string? apiKey = null)
    {
        using var package = TestPackages.Build(id, version);
        using var request = TestPackages.PushRequest(package, apiKey);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private IEnumerable<string> EveryTagValue()
        => _capture.Stopped.SelectMany(a => a.TagObjects).Select(t => t.Value?.ToString() ?? string.Empty)
            .Concat(_capture.Measurements.SelectMany(m => m.Tags.Values).Select(v => v?.ToString() ?? string.Empty));

    [Fact]
    public async Task APushEmitsAPackageIndexSpanAndCountsIt()
    {
        var (_, client) = Start(new Dictionary<string, string> { ["ApiKey"] = ApiKey });

        Assert.Equal(HttpStatusCode.Created, await PushAsync(client, "Telemetry.Push", "1.0.0", ApiKey));

        var span = Assert.Single(_capture.Spans("package.index"));
        Assert.Equal("Telemetry.Push", DiagnosticsCapture.Tag(span, BaGetDiagnostics.Tags.PackageId));
        Assert.Equal("1.0.0", DiagnosticsCapture.Tag(span, BaGetDiagnostics.Tags.PackageVersion));
        Assert.Equal("Success", DiagnosticsCapture.Tag(span, BaGetDiagnostics.Tags.Result));

        var pushed = Assert.Single(_capture.Counts("baget.packages.pushed"));
        Assert.Equal("Success", pushed.Tags[BaGetDiagnostics.Tags.Result]);

        // Never the API key, whichever tag.
        Assert.DoesNotContain(EveryTagValue(), v => v.Contains(ApiKey));
    }

    [Fact]
    public async Task ADownloadIsCountedOverHttp()
    {
        var (_, client) = Start();
        Assert.Equal(HttpStatusCode.Created, await PushAsync(client, "Telemetry.Download", "1.0.0"));

        using var response = await client.GetAsync("v3/package/telemetry.download/1.0.0/telemetry.download.1.0.0.nupkg", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _capture.Counts("baget.downloads").Sum(m => m.Value));
    }

    [Fact]
    public async Task AValidatedPushEmitsAValidateSpanThatParentsTheIndexSpanAndCountsTheResult()
    {
        var (_, client) = Start(new Dictionary<string, string> { ["Validation:Enabled"] = "true" });
        Assert.Equal(HttpStatusCode.Accepted, await PushAsync(client, "Telemetry.Validated", "2.0.0"));

        await WaitForAsync(() => _capture.Counts("baget.validation.results").Count > 0);

        var validate = Assert.Single(_capture.Spans("package.validate"), s => s.GetTagItem(BaGetDiagnostics.Tags.PackageId) != null);
        Assert.Equal("Telemetry.Validated", DiagnosticsCapture.Tag(validate, BaGetDiagnostics.Tags.PackageId));
        Assert.Equal("2.0.0", DiagnosticsCapture.Tag(validate, BaGetDiagnostics.Tags.PackageVersion));
        Assert.Equal("Succeeded", DiagnosticsCapture.Tag(validate, BaGetDiagnostics.Tags.Result));

        var index = Assert.Single(_capture.Spans("package.index"));
        Assert.Equal(validate.Id, index.ParentId);
        Assert.Equal(validate.TraceId, index.TraceId);

        var result = Assert.Single(_capture.Counts("baget.validation.results"));
        Assert.Equal("Succeeded", result.Tags[BaGetDiagnostics.Tags.Result]);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The condition was not met.");
            await Task.Delay(50);
        }
    }

    [Fact]
    public void TheCatalogShowsTheTelemetrySection()
    {
        var (app, _) = Start();
        var catalog = app.Services.GetRequiredService<SettingsCatalog>();
        var telemetry = catalog.InSection("Telemetry").ToDictionary(d => d.Key);

        foreach (var key in new[]
        {
            "Telemetry:Enabled", "Telemetry:ServiceName", "Telemetry:Otlp:Endpoint", "Telemetry:Otlp:Protocol",
            "Telemetry:Otlp:Headers", "Telemetry:Otlp:Timeout", "Telemetry:Traces:Enabled", "Telemetry:Traces:SamplingRatio",
            "Telemetry:Traces:Database", "Telemetry:Metrics:Endpoint", "Telemetry:Metrics:ExportInterval",
            "Telemetry:Logs:Endpoint", "Telemetry:Logs:MinimumLevel",
        })
        {
            Assert.True(telemetry.ContainsKey(key), key + " is missing");
        }

        Assert.True(telemetry["Telemetry:Otlp:Headers"].IsSensitive);
        Assert.Single(telemetry.Values, d => d.IsSensitive);
        Assert.Equal(SettingKind.Enum, telemetry["Telemetry:Otlp:Protocol"].Kind);
        Assert.Equal(new[] { "Grpc", "HttpProtobuf" }, telemetry["Telemetry:Otlp:Protocol"].EnumValues);
        Assert.Equal(SettingKind.TimeSpan, telemetry["Telemetry:Otlp:Timeout"].Kind);
        Assert.Equal(SettingKind.TimeSpan, telemetry["Telemetry:Metrics:ExportInterval"].Kind);
        Assert.Equal(SettingKind.Number, telemetry["Telemetry:Traces:SamplingRatio"].Kind);
        Assert.Equal(SettingKind.Boolean, telemetry["Telemetry:Enabled"].Kind);
        Assert.Equal(SettingKind.Enum, telemetry["Telemetry:Logs:MinimumLevel"].Kind);

        var endpoint = telemetry["Telemetry:Otlp:Endpoint"];
        Assert.Equal("Telemetry:Otlp:Protocol", endpoint.ExampleDependsOnKey);
        Assert.Equal("http://jaeger:4317", endpoint.Examples!["Grpc"]);
        Assert.Equal("http://jaeger:4318", endpoint.Examples["HttpProtobuf"]);

        // Applied live, except the two read once when the exporter is built.
        var restart = new[] { "Telemetry:ServiceName", "Telemetry:Metrics:ExportInterval" };
        Assert.All(telemetry.Values, d => Assert.Equal(restart.Contains(d.Key), d.RequiresRestart));
        Assert.All(telemetry.Values, d => Assert.NotEmpty(d.Description));
    }

    [Fact]
    public void StartupValidationRejectsABadTelemetryEndpointAndRatio()
    {
        var validator = new ConfigureBaGetOptions(new ConfigurationBuilder().Build());
        var options = new BaGetOptions
        {
            Database = new DatabaseOptions { Type = "Sqlite" },
            Mirror = new MirrorOptions(),
            Search = new SearchOptions { Type = "Database" },
            Storage = new StorageOptions { Type = "FileSystem" },
            Telemetry = new TelemetryOptions(),
        };
        Assert.True(validator.Validate(null, options).Succeeded);

        options.Telemetry.Otlp.Endpoint = "jaeger:4317";
        options.Telemetry.Traces.SamplingRatio = 2;
        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Telemetry:Otlp:Endpoint", result.FailureMessage);
        Assert.Contains("SamplingRatio", result.FailureMessage);
    }
}
