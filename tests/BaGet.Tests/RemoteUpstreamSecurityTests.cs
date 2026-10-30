using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>An <see cref="IRemoteHttpClientFactory"/> whose upstream is a script; it records every URL asked.</summary>
internal sealed class ScriptedRemoteHttpClientFactory : RemoteHttpClientFactory
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _script;
    private readonly List<Uri> _requests = new();

    public ScriptedRemoteHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> script)
    {
        _script = script;
    }

    public Uri[] Requests
    {
        get
        {
            lock (_requests) return _requests.ToArray();
        }
    }

    protected override HttpMessageHandler CreateInnerHandler() => new Handler(this);

    private sealed class Handler : HttpMessageHandler
    {
        private readonly ScriptedRemoteHttpClientFactory _owner;

        public Handler(ScriptedRemoteHttpClientFactory owner) => _owner = owner;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_owner._requests) _owner._requests.Add(request.RequestUri!);

            return Task.FromResult(_owner._script(request));
        }
    }
}

/// <summary>
/// What an upstream URL can make the server do (SSRF): Test connection does not follow a redirect to another host and
/// reads a bounded answer, and <c>Remote:BlockPrivateAddresses</c> refuses loopback, private and link-local upstreams
/// when a repository is created or saved and when a connection is tested.
/// </summary>
public class RemoteUpstreamSecurityTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;
    private const string Index = "{\"version\":\"3.0.0\",\"resources\":[{\"@id\":\"x\",\"@type\":\"SearchQueryService\"}]}";

    private static BaGetApplication App(ITestOutputHelper output, ScriptedRemoteHttpClientFactory wire, bool block)
        => new(
            output,
            configOverrides: block ? new Dictionary<string, string> { { "Remote:BlockPrivateAddresses", "true" } } : null,
            configureServices: services => services.AddSingleton<IRemoteHttpClientFactory>(wire));

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static async Task<RemoteConnectionResult> TestAsync(BaGetApplication app, string url)
    {
        using var scope = app.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>()
            .TestConnectionAsync(null, new RemoteRepositorySettings { Url = url }, Cancel);
    }

    [Fact]
    public async Task TestConnectionDoesNotFollowARedirectToAnotherHost()
    {
        var wire = new ScriptedRemoteHttpClientFactory(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("http://169.254.169.254/latest/meta-data/");

            return response;
        });
        using var app = App(output, wire, block: false);

        var result = await TestAsync(app, "http://feed.example/v3/index.json");

        Assert.False(result.Success);
        Assert.Contains("redirects to another host", result.Message);
        Assert.DoesNotContain("169.254", result.Message);
        Assert.Equal(new[] { new Uri("http://feed.example/v3/index.json") }, wire.Requests);
    }

    [Fact]
    public async Task TestConnectionFollowsARedirectWithinTheSameOrigin()
    {
        var wire = new ScriptedRemoteHttpClientFactory(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v3/index.json")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
                redirect.Headers.Location = new Uri("/v3/final.json", UriKind.Relative);

                return redirect;
            }

            return Json(Index);
        });
        using var app = App(output, wire, block: false);

        var result = await TestAsync(app, "http://feed.example/v3/index.json");

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, wire.Requests.Length);
    }

    [Fact]
    public async Task TestConnectionReadsAtMostAMegabyteOfTheAnswer()
    {
        var wire = new ScriptedRemoteHttpClientFactory(_ => Json(new string('x', 2 * 1024 * 1024)));
        using var app = App(output, wire, block: false);

        var result = await TestAsync(app, "http://feed.example/v3/index.json");

        Assert.False(result.Success);
        Assert.Contains("over 1 MB", result.Message);
    }

    [Theory]
    [InlineData("http://127.0.0.1/v3/index.json")]
    [InlineData("http://localhost/v3/index.json")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://10.1.2.3/index.json")]
    [InlineData("http://192.168.0.7/index.json")]
    [InlineData("http://172.20.0.1/index.json")]
    [InlineData("http://[::1]/index.json")]
    [InlineData("http://[fe80::1]/index.json")]
    [InlineData("http://[fd00::1]/index.json")]
    [InlineData("http://[::ffff:10.0.0.1]/index.json")]
    public async Task APrivateUpstreamIsRefusedWhenBlockedAndNeverContacted(string url)
    {
        var wire = new ScriptedRemoteHttpClientFactory(_ => Json(Index));
        using var app = App(output, wire, block: true);
        using var scope = app.Services.CreateScope();

        var repositories = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
        var create = await Assert.ThrowsAsync<ArgumentException>(() => repositories.CreateAsync(
            "up", RepositoryFormat.NuGet, RepositoryKind.Remote, new RemoteRepositorySettings { Url = url }, Cancel));
        Assert.Contains("Remote:BlockPrivateAddresses", create.Message);

        await repositories.CreateAsync("ok", RepositoryFormat.NuGet, RepositoryKind.Remote, new RemoteRepositorySettings { Url = "http://feed.example/v3/index.json" }, Cancel);
        var administration = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();
        await Assert.ThrowsAsync<ArgumentException>(() => administration.UpdateAsync("ok", new RemoteRepositorySettings { Url = url }, clearPassword: false, Cancel));

        var test = await TestAsync(app, url);
        Assert.False(test.Success);
        Assert.Contains("Remote:BlockPrivateAddresses", test.Message);
        Assert.Empty(wire.Requests);
    }

    [Fact]
    public async Task APrivateUpstreamIsAllowedByDefault()
    {
        var wire = new ScriptedRemoteHttpClientFactory(_ => Json(Index));
        using var app = App(output, wire, block: false);
        using var scope = app.Services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IRepositoryService>().CreateAsync(
            "internal", RepositoryFormat.NuGet, RepositoryKind.Remote, new RemoteRepositorySettings { Url = "http://10.1.2.3/v3/index.json" }, Cancel);

        Assert.True((await TestAsync(app, "http://127.0.0.1/v3/index.json")).Success);
    }

    [Theory]
    [InlineData("8.8.8.8", false)]
    [InlineData("1.1.1.1", false)]
    [InlineData("172.15.0.1", false)]
    [InlineData("172.32.0.1", false)]
    [InlineData("100.63.0.1", false)]
    [InlineData("2606:4700::1111", false)]
    [InlineData("100.64.0.1", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("224.0.0.1", true)]
    [InlineData("::", true)]
    [InlineData("fc00::1", true)]
    [InlineData("::ffff:127.0.0.1", true)]
    [InlineData("64:ff9b::a9fe:a9fe", true)]
    [InlineData("64:ff9b::808:808", false)]
    [InlineData("64:ff9b:1::1", true)]
    [InlineData("2002:a9fe:a9fe::1", true)]
    [InlineData("2002:808:808::1", false)]
    [InlineData("::169.254.169.254", true)]
    [InlineData("::8.8.8.8", false)]
    public void AddressClassification(string address, bool blocked)
        => Assert.Equal(blocked, RemoteAddressPolicy.IsBlocked(IPAddress.Parse(address)));

    private readonly ITestOutputHelper output;

    public RemoteUpstreamSecurityTests(ITestOutputHelper output)
    {
        this.output = output;
    }
}
