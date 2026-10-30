using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// A reverse proxy may either forward the path base to BaGet, or strip it before
/// forwarding. The URLs BaGet advertises must include the configured path base
/// either way, otherwise clients are sent to addresses that bypass the proxy.
/// </summary>
public class PathBaseIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public PathBaseIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string>
            {
                { "PathBase", "/nuget" },
            });

        _client = _app.CreateDefaultClient();
    }

    [Fact]
    public async Task ServiceIndexUsesPathBaseWhenTheProxyStripsIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // The proxy rewrote "/nuget/v3/index.json" to "/v3/index.json".
        using var response = await _client.GetAsync("v3/index.json", cancellationToken);

        response.EnsureSuccessStatusCode();
        var content = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        #pragma warning disable CS8602
        foreach (var resource in content["resources"])
        #pragma warning restore CS8602
        {
            #pragma warning disable CS8604
            var id = resource["@id"].Value<string>();
            #pragma warning restore CS8604
            Assert.StartsWith("http://localhost/nuget/", id);
        }
    }

    [Fact]
    public async Task ServiceIndexUsesPathBaseWhenTheProxyForwardsIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // The proxy forwarded the path base untouched.
        using var response = await _client.GetAsync("nuget/v3/index.json", cancellationToken);

        response.EnsureSuccessStatusCode();
        var content = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        #pragma warning disable CS8602
        foreach (var resource in content["resources"])
        #pragma warning restore CS8602
        {
            #pragma warning disable CS8604
            var id = resource["@id"].Value<string>();
            #pragma warning restore CS8604
            Assert.StartsWith("http://localhost/nuget/", id);
        }
    }


    [Fact]
    public async Task HeaderLinksCarryThePathBase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync("nuget/stats", cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Contains("class=\"navbar-brand brand\" href=\"/nuget/\"", html);
        Assert.Contains("<a href=\"/nuget/packages\" role=\"tab\"", html);
        Assert.DoesNotContain("href=\"/packages\"", html);
        Assert.Contains("<meta name=\"author\" content=\"Loic Sharma\">", html);
    }

    [Fact]
    public async Task ADotPathBaseMeansTheRoot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // appsettings.json ships "PathBase": ".", which is not a usable PathString.
        using var app = new BaGetApplication(
            _output,
            configOverrides: new Dictionary<string, string> { { "PathBase", "." } });
        using var client = app.CreateDefaultClient();

        using var response = await client.GetAsync("v3/index.json", cancellationToken);

        response.EnsureSuccessStatusCode();
        var content = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        #pragma warning disable CS8602
        foreach (var resource in content["resources"])
        #pragma warning restore CS8602
        {
            #pragma warning disable CS8604
            Assert.StartsWith("http://localhost/", resource["@id"].Value<string>());
            #pragma warning restore CS8604
        }
    }

    [Fact]
    public async Task APathBaseMissingItsLeadingSlashStillApplies()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var app = new BaGetApplication(
            _output,
            configOverrides: new Dictionary<string, string> { { "PathBase", "nuget" } });
        using var client = app.CreateDefaultClient();

        using var response = await client.GetAsync("v3/index.json", cancellationToken);

        response.EnsureSuccessStatusCode();
        var content = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        #pragma warning disable CS8602
        foreach (var resource in content["resources"])
        #pragma warning restore CS8602
        {
            #pragma warning disable CS8604
            Assert.StartsWith("http://localhost/nuget/", resource["@id"].Value<string>());
            #pragma warning restore CS8604
        }
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(".", "")]
    [InlineData("/", "")]
    [InlineData("/nuget", "/nuget")]
    [InlineData("nuget", "/nuget")]
    [InlineData("/nuget/", "/nuget")]
    [InlineData("/nuget/feed", "/nuget/feed")]
    public void NormalizePathBaseMapsConfiguredValues(string? configured, string expected)
    {
        Assert.Equal(expected, configured.NormalizePathBase().Value ?? "");
    }

    public void Dispose()
    {
        _client?.Dispose();
        _app?.Dispose();
    }
}