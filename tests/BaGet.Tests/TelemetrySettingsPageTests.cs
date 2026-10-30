using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Core.Tests.Support;
using BaGet.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// The Telemetry section of the Settings page: the per-signal status panel and "Test connection". Joins the Diagnostics
/// collection (runs alone) because it builds a telemetry-enabled host, whose process-wide listeners would otherwise see
/// other hosts' spans.
/// </summary>
[Collection(DiagnosticsCapture.Name)]
public class TelemetrySettingsPageTests : IDisposable
{
    private const string Password = "Passw0rd!";
    private const string TestAction = "admin/settings?handler=TestTelemetry";
    private const string Secret = "TOP-SECRET-HEADER-VALUE";

    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public TelemetrySettingsPageTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }

    private async Task<(BaGetApplication App, Browser Admin, InMemoryTelemetry Telemetry)> StartAsync(Dictionary<string, string>? config = null)
    {
        InMemoryTelemetry? telemetry = null;
        var settings = new Dictionary<string, string> { ["Accounts:Enabled"] = "true" };
        foreach (var pair in config ?? new Dictionary<string, string>()) settings[pair.Key] = pair.Value;

        var app = new BaGetApplication(_output, configOverrides: settings, configureServices: services => telemetry = InMemoryTelemetry.Install(services));
        _disposables.Add(app);
        var admin = new Browser(app);
        _disposables.Add(admin);

        await admin.RegisterAsync("root");
        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AdministratorService>()
                .PromoteAsync("root", "test", System.Threading.CancellationToken.None);
        }

        return (app, admin, telemetry!);
    }

    private static Task<HttpResponseMessage> PostTestAsync(Browser browser, string? endpoint, string password = Password, string? headers = null, string? protocol = "Grpc", bool clearHeaders = false)
    {
        var fields = new Dictionary<string, string> { ["section"] = "Telemetry", ["password"] = password };
        if (endpoint != null) fields["v:Telemetry:Otlp:Endpoint"] = endpoint;
        if (protocol != null) fields["v:Telemetry:Otlp:Protocol"] = protocol;
        if (headers != null) fields["v:Telemetry:Otlp:Headers"] = headers;
        if (clearHeaders) fields["clear:Telemetry:Otlp:Headers"] = "true";

        return browser.PostAsync("admin/settings", fields, TestAction);
    }

    private static async Task<string> BodyAsync(HttpResponseMessage response)
    {
        using (response) return await response.Content.ReadAsStringAsync();
    }

    private static (TcpListener Listener, string Url) Listen()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return (listener, "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port);
    }

    private static string ClosedPortUrl()
    {
        var (listener, url) = Listen();
        listener.Stop();
        return url;
    }

    private static async Task SaveAsync(BaGetApplication app, params (string Key, object Value)[] changes)
    {
        var service = app.Services.GetRequiredService<SettingsService>();
        var result = await service.SaveAsync(changes.ToDictionary(c => service.Catalog.Find(c.Key)!, c => c.Value), "test", TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    [Fact]
    public async Task TheTestIsAdministratorsOnly()
    {
        var (app, _, telemetry) = await StartAsync();
        using var alice = new Browser(app);
        await alice.RegisterAsync("alice");

        using var response = await alice.PostAsync(
            "account/settings",
            new Dictionary<string, string> { ["password"] = Password, ["v:Telemetry:Otlp:Endpoint"] = "http://127.0.0.1:1" },
            TestAction);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, telemetry.TraceExportersCreated);
    }

    [Fact]
    public async Task ThePostNeedsAnAntiforgeryToken()
    {
        var (_, admin, telemetry) = await StartAsync();

        using var response = await admin.RawPostAsync(TestAction, new Dictionary<string, string>
        {
            ["password"] = Password,
            ["v:Telemetry:Otlp:Endpoint"] = ClosedPortUrl(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, telemetry.TraceExportersCreated);
    }

    [Fact]
    public async Task AfterTestConnectionTheTelemetrySectionIsShown()
    {
        var (_, admin, _) = await StartAsync();

        var html = await BodyAsync(await PostTestAsync(admin, ClosedPortUrl()));

        Assert.Contains("telemetry-test-result", html);
        Assert.Equal(new[] { "Telemetry" }, SettingsSectionPageTests.Panels(html).Where(p => !p.Hidden).Select(p => p.Id));
        Assert.Equal(new[] { "Telemetry" }, SettingsSectionPageTests.NavItems(html).Where(n => n.Active).Select(n => n.Id));
    }

    [Fact]
    public async Task AWrongPasswordTestsNothing()
    {
        var (_, admin, telemetry) = await StartAsync();

        var html = await BodyAsync(await PostTestAsync(admin, ClosedPortUrl(), password: "wrong"));

        Assert.Contains("nothing was tested", html);
        Assert.DoesNotContain("telemetry-test-result", html);
        Assert.Equal(0, telemetry.TraceExportersCreated);
    }

    [Fact]
    public async Task ThePostedUnsavedValuesAreTestedAndNothingIsSaved()
    {
        var (app, admin, telemetry) = await StartAsync();
        var (listener, url) = Listen();
        try
        {
            var html = await BodyAsync(await PostTestAsync(admin, url, headers: "x-key=" + Secret, protocol: "HttpProtobuf"));

            Assert.Contains("id=\"telemetry-test-result\"", html);
            Assert.Contains("Connection ok", html);
            Assert.Contains("Nothing was saved", html);
            var options = Assert.Single(telemetry.TraceOptions);
            Assert.Equal(url, options.Otlp.Endpoint);
            Assert.Equal("HttpProtobuf", options.Otlp.Protocol);
            Assert.Equal("x-key=" + Secret, options.Otlp.Headers);
            Assert.DoesNotContain(Secret, html);
        }
        finally
        {
            listener.Stop();
        }

        var settings = app.Services.GetRequiredService<SettingsService>();
        Assert.False(System.IO.File.Exists(settings.OverridesPath), "testing wrote the overrides file");
        var current = app.Services.GetRequiredService<IOptionsMonitor<TelemetryOptions>>().CurrentValue;
        Assert.False(current.Enabled);
        Assert.NotEqual(url, current.Otlp.Endpoint);
        Assert.All(app.Services.GetRequiredService<TelemetryStatus>().GetAll(), s => Assert.False(s.Active));
    }

    [Fact]
    public async Task AFailureShowsAScrubbedError()
    {
        var (_, admin, _) = await StartAsync();

        var html = await BodyAsync(await PostTestAsync(admin, ClosedPortUrl() + "/private/path?token=QUERY-SECRET", headers: "x-key=" + Secret));

        Assert.Contains("Connection failed", html);
        Assert.Contains("alert-danger", html);
        Assert.DoesNotContain(Secret, html);
        Assert.DoesNotContain("QUERY-SECRET", html);
        Assert.DoesNotContain("/private/path", html);
    }

    [Fact]
    public async Task ABadEndpointIsReportedWithoutAnyConnection()
    {
        var (_, admin, telemetry) = await StartAsync();

        var html = await BodyAsync(await PostTestAsync(admin, "not a url"));

        Assert.Contains("Connection failed", html);
        Assert.Contains("absolute http or https endpoint", html);
        Assert.Equal(0, telemetry.TraceExportersCreated);
    }

    [Fact]
    public async Task ABlankHeadersFieldKeepsTheSavedHeadersAndClearDropsThem()
    {
        var (app, admin, telemetry) = await StartAsync();
        await SaveAsync(app, ("Telemetry:Otlp:Headers", "x-key=" + Secret));
        var (listener, url) = Listen();
        try
        {
            var blank = await BodyAsync(await PostTestAsync(admin, url, headers: ""));
            var cleared = await BodyAsync(await PostTestAsync(admin, url, headers: "", clearHeaders: true));

            Assert.Contains("Connection ok", blank);
            Assert.Contains("Connection ok", cleared);
            Assert.DoesNotContain(Secret, blank + cleared);
            Assert.Equal(new[] { "x-key=" + Secret, string.Empty }, telemetry.TraceOptions.Select(o => o.Otlp.Headers ?? string.Empty).ToArray());
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ThePanelShowsOffByDefault()
    {
        var (_, admin, _) = await StartAsync();

        var html = await admin.GetPageAsync("admin/settings");

        Assert.Contains("id=\"telemetry-status\"", html);
        Assert.Equal(3, Regex.Matches(html, "data-state=\"Off\"").Count);
        Assert.Contains("id=\"telemetry-test\"", html);
        Assert.DoesNotContain("telemetry-test-result", html);
    }

    [Fact]
    public async Task ThePanelShowsNotConfiguredWithoutAnEndpoint()
    {
        var (_, admin, _) = await StartAsync(new Dictionary<string, string> { ["Telemetry:Enabled"] = "true", ["Telemetry:Otlp:Endpoint"] = "" });

        var html = await admin.GetPageAsync("admin/settings");

        Assert.Equal(3, Regex.Matches(html, "data-state=\"NotConfigured\"").Count);
        Assert.Contains("Not configured", html);
    }

    [Fact]
    public async Task ThePanelFollowsASignalFromWaitingToOkToFailingAndEncodesTheError()
    {
        var (app, admin, _) = await StartAsync(new Dictionary<string, string>
        {
            ["Telemetry:Enabled"] = "true",
            ["Telemetry:Otlp:Endpoint"] = "http://collector.invalid:4317",
        });
        var status = app.Services.GetRequiredService<TelemetryStatus>();

        static string Row(string html, string signal)
            => Regex.Match(html, "<tr data-signal=\"" + signal + "\".*?</tr>", RegexOptions.Singleline).Value;

        var page = await admin.GetPageAsync("admin/settings");
        Assert.Contains("data-state=\"Waiting\"", Row(page, "Traces"));
        Assert.Contains("data-state=\"NotConfigured\"", Row(page, "Metrics"));

        status.Traces.Success();
        var ok = Row(await admin.GetPageAsync("admin/settings"), "Traces");
        Assert.Contains("data-state=\"Ok\"", ok);
        Assert.Contains("Last export ok at", ok);

        status.Traces.Failure(TelemetryStatus.Scrub("boom <script>alert(1)</script> to http://u:p@collector.invalid:4317/v1/traces?k=v"));
        var failing = Row(await admin.GetPageAsync("admin/settings"), "Traces");
        Assert.Contains("data-state=\"Failing\"", failing);
        Assert.Contains("Failing since", failing);
        Assert.Contains("&lt;script&gt;", failing);
        Assert.DoesNotContain("<script>alert", failing);
        Assert.DoesNotContain("u:p@", failing);
        Assert.DoesNotContain("k=v", failing);
    }

    [Fact]
    public async Task AHostThatExportsNothingShowsANoteButKeepsTheSectionEditable()
    {
        var app = new BaGetApplication(
            _output,
            configOverrides: new Dictionary<string, string> { ["Accounts:Enabled"] = "true" },
            configureServices: services => services.RemoveAll<ITelemetryDiagnostics>());
        _disposables.Add(app);
        var admin = new Browser(app);
        _disposables.Add(admin);
        await admin.RegisterAsync("root");
        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AdministratorService>()
                .PromoteAsync("root", "test", System.Threading.CancellationToken.None);
        }

        var html = await admin.GetPageAsync("admin/settings");

        Assert.Contains("id=\"telemetry-no-host\"", html);
        Assert.Contains("export telemetry", html);
        Assert.DoesNotContain("id=\"telemetry-status\"", html);
        Assert.DoesNotContain("id=\"telemetry-test\"", html);
        Assert.Contains("v:Telemetry:Otlp:Endpoint", html);
    }

    [Fact]
    public async Task AHostThatExportsTelemetryShowsNoNote()
    {
        var (_, admin, _) = await StartAsync();

        var html = await admin.GetPageAsync("admin/settings");

        Assert.DoesNotContain("telemetry-no-host", html);
        Assert.Contains("id=\"telemetry-status\"", html);
    }
}
