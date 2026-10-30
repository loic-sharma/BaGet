using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;
using Xunit;
using static BaGet.Tests.AdminTestSupport;

namespace BaGet.Tests;

/// <summary>
/// ASP.NET Core's hosting scope puts the raw request path on every event logged during a request, so npm logout's token (the
/// last path segment) must be masked on the event itself: in every sink and in the OTLP log export, not only in the request line.
/// </summary>
public class LogScopeRedactionTests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly CapturingSink _sink = new();
    private InMemoryTelemetry _telemetry = null!;

    public LogScopeRedactionTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string>
            {
                { "ApiKey", "global-key" },
                { "Accounts:Enabled", "true" },
                { "Telemetry:Enabled", "true" },
                { "Telemetry:Otlp:Endpoint", "http://collector.invalid:4317" },
                { "Telemetry:Logs:Endpoint", "http://collector.invalid:4317" },
            },
            configureServices: services =>
            {
                _telemetry = InMemoryTelemetry.Install(services);
                services.AddSingleton<ILogEventSink>(_sink);
            });
    }

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task ALogoutTokenReachesNoSinkNorTheOtlpLogExport()
    {
        var ct = TestContext.Current.CancellationToken;
        await _app.CreateNpmRepositoryAsync("web", cancellationToken: ct);
        await CreateUserAsync(_app, "alice");
        using var client = _app.CreateClient();
        var login = new JsonObject { ["_id"] = "org.couchdb.user:alice", ["name"] = "alice", ["password"] = Password, ["type"] = "user" };
        using var loginRequest = NpmTestPackages.Json(HttpMethod.Put, "/r/web/npm/-/user/org.couchdb.user:alice", login.ToJsonString(), null);
        using var loginResponse = await client.SendAsync(loginRequest, ct);
        Assert.Equal(HttpStatusCode.Created, loginResponse.StatusCode);
        var token = (string)JsonNode.Parse(await loginResponse.Content.ReadAsStringAsync(ct))!["token"]!;

        // Revoking writes the database; while it is closed the refusal is a Warning logged inside the request's scope.
        _app.Services.GetRequiredService<MaintenanceGate>().CloseUntilRestart("test");
        using var logoutRequest = NpmTestPackages.Json(HttpMethod.Delete, "/r/web/npm/-/user/token/" + token, null, token);
        using var logout = await client.SendAsync(logoutRequest, ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, logout.StatusCode);

        InMemoryTelemetry.Flush(_app.Services);
        var warning = Assert.Single(_sink.Events, e => e.Level == LogEventLevel.Warning && e.MessageTemplate.Text.StartsWith("Write refused"));
        Assert.Equal("/r/web/npm/-/user/token/Redacted", ((ScalarValue)warning.Properties["RequestPath"]).Value);
        Assert.DoesNotContain(_sink.Events, e => e.Properties.Values.Any(v => v.ToString().Contains(token)) || e.RenderMessage().Contains(token));
        Assert.NotEmpty(_telemetry.Logs);
        Assert.DoesNotContain(_telemetry.Logs, e => e.Properties.Values.Any(v => v.ToString().Contains(token)) || e.RenderMessage().Contains(token));
    }

    private sealed class CapturingSink : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _events = new();

        public IReadOnlyList<LogEvent> Events => _events.ToArray();

        public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
    }
}
