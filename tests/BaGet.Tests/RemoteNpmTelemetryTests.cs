using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Core.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// A remote npm repository's fetches emit the same <c>mirror.fetch</c> span as a NuGet mirror, with the package and the result and
/// never a URL or a credential. Joins the Diagnostics collection (the sources are process-wide).
/// </summary>
[Collection(DiagnosticsCapture.Name)]
public class RemoteNpmTelemetryTests : IDisposable
{
    private readonly FakeNpmUpstream _upstream = new();
    private readonly DiagnosticsCapture _capture = new();
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public RemoteNpmTelemetryTests(ITestOutputHelper output)
    {
        var wire = new ForwardingRemoteHttpClientFactory(new HttpClient(_upstream));
        _app = new BaGetApplication(output, configureServices: services => services.AddSingleton<IRemoteHttpClientFactory>(wire));
        _client = _app.CreateClient();
        using var scope = _app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepositoryService>().CreateAsync(
            "up", RepositoryFormat.Npm, RepositoryKind.Remote, new RemoteRepositorySettings { Url = "http://registry.test/", Password = null }, anonymousRead: true, default)
            .GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
        _capture.Dispose();
    }

    [Fact]
    public async Task AFetchEmitsAMirrorFetchSpanWithThePackageAndTheResult()
    {
        _upstream.Add("left-pad", "1.0.0");

        using var response = await _client.GetAsync("r/up/npm/left-pad/-/left-pad-1.0.0.tgz", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var span = Assert.Single(_capture.Spans("mirror.fetch"));
        Assert.Equal("left-pad", DiagnosticsCapture.Tag(span, BaGetDiagnostics.Tags.PackageId));
        Assert.Equal("1.0.0", DiagnosticsCapture.Tag(span, BaGetDiagnostics.Tags.PackageVersion));
        Assert.Equal("Success", DiagnosticsCapture.Tag(span, BaGetDiagnostics.Tags.Result));
        Assert.DoesNotContain(_capture.Stopped.SelectMany(a => a.TagObjects), t => (t.Value?.ToString() ?? string.Empty).Contains("registry.test"));

        // Served from storage afterwards: no second fetch.
        using var again = await _client.GetAsync("r/up/npm/left-pad/-/left-pad-1.0.0.tgz", TestContext.Current.CancellationToken);
        Assert.Single(_capture.Spans("mirror.fetch"));
    }

    [Fact]
    public async Task ARejectedDownloadMarksTheSpanAsFailedWithTheExceptionTypeOnly()
    {
        _upstream.Add("left-pad", "1.0.0");
        _upstream.Corrupt.Add("left-pad@1.0.0");

        using var response = await _client.GetAsync("r/up/npm/left-pad/-/left-pad-1.0.0.tgz", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var span = Assert.Single(_capture.Spans("mirror.fetch"));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.DoesNotContain(span.TagObjects, t => (t.Value?.ToString() ?? string.Empty).Contains("integrity", StringComparison.OrdinalIgnoreCase));
    }
}
