using System.Collections.Generic;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// Every absolute URL BaGet advertises is built from the incoming request's "Host"
/// header, so a forged host decides where clients are sent. The host must therefore
/// be checked against the configured allow-list before a request reaches a handler,
/// and the default must stay permissive: BaGet is routinely run behind a reverse
/// proxy on an arbitrary host name.
/// </summary>
public class AllowedHostsIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public AllowedHostsIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ServesAnAllowedHost()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var app = CreateApplication("baget.example.com");
        using var client = app.CreateDefaultClient();

        using var response = await client.GetAsync("http://baget.example.com/v3/index.json", cancellationToken);

        response.EnsureSuccessStatusCode();
        var content = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        #pragma warning disable CS8602
        foreach (var resource in content["resources"])
        #pragma warning restore CS8602
        {
            #pragma warning disable CS8604
            Assert.StartsWith(
                "http://baget.example.com/",
                resource["@id"].Value<string>());
            #pragma warning restore CS8604
        }
    }

    [Fact]
    public async Task RejectsAHostThatIsNotAllowed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var app = CreateApplication("baget.example.com");
        using var client = app.CreateDefaultClient();

        using var response = await client.GetAsync("http://attacker.example.com/v3/index.json", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ServesAnyHostWhenNothingIsConfigured()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var app = CreateApplication(allowedHosts: null);
        using var client = app.CreateDefaultClient();

        using var response = await client.GetAsync("http://anything.example.com/v3/index.json", cancellationToken);

        response.EnsureSuccessStatusCode();
        var content = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        #pragma warning disable CS8602
        foreach (var resource in content["resources"])
        #pragma warning restore CS8602
        {
            #pragma warning disable CS8604
            Assert.StartsWith(
                "http://anything.example.com/",
                resource["@id"].Value<string>());
            #pragma warning restore CS8604
        }
    }

    [Fact]
    public async Task ServesEveryHostOfAWhitespaceSeparatedList()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // A semicolon separated list is normally written with spaces after the
        // separators. Those entries must not become unmatchable patterns.
        using var app = CreateApplication("baget.example.com; cdn.example.com");
        using var client = app.CreateDefaultClient();

        using var allowed = await client.GetAsync("http://cdn.example.com/v3/index.json", cancellationToken);
        using var rejected = await client.GetAsync("http://attacker.example.com/v3/index.json", cancellationToken);

        allowed.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Fact]
    public async Task RejectsAForgedHostWithoutTheHostBuilderDefaults()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // The default builder happens to add the host filtering middleware, but BaGet
        // must not depend on it: a consumer of AddBaGetWebApplication may build its host
        // with a slim or empty builder, which does not add it. With the builder's own
        // copy removed, a rejection here can only come from BaGet's pipeline.
        using var app = WithoutTheBuildersHostFiltering(
            _output,
            new Dictionary<string, string> { { "AllowedHosts", "baget.example.com" } });
        using var client = app.CreateDefaultClient();

        using var allowed = await client.GetAsync("http://baget.example.com/v3/index.json", cancellationToken);
        using var rejected = await client.GetAsync("http://attacker.example.com/v3/index.json", cancellationToken);

        allowed.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Theory]
    [InlineData(null, "*")]
    [InlineData("", "*")]
    [InlineData("   ", "*")]
    [InlineData(";;", "*")]
    [InlineData("*", "*")]
    [InlineData("baget.example.com", "baget.example.com")]
    [InlineData(" baget.example.com ", "baget.example.com")]
    [InlineData("a.example.com; b.example.com", "a.example.com|b.example.com")]
    [InlineData("a.example.com;;b.example.com", "a.example.com|b.example.com")]
    [InlineData("a.example.com;A.EXAMPLE.COM", "a.example.com")]
    [InlineData("*.example.com;example.com:8080", "*.example.com|example.com:8080")]
    public void ParseAllowedHostsMapsConfiguredValues(string? configured, string expected)
    {
        var hosts = ConfigureBaGetOptions.ParseAllowedHosts(configured);

        Assert.Equal(expected, string.Join("|", hosts));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("*", true)]
    [InlineData("baget.example.com", false)]
    public void EmptyHostsAreOnlyToleratedWhileFilteringIsDisabled(
        string? allowedHosts,
        bool expected)
    {
        #pragma warning disable CS8620
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { nameof(BaGetOptions.AllowedHosts), allowedHosts },
            })
            .Build();
        #pragma warning restore CS8620
        var options = new HostFilteringOptions();

        new ConfigureBaGetOptions(configuration).Configure(options);

        Assert.Equal(expected, options.AllowEmptyHosts);
    }

    private BaGetApplication CreateApplication(string? allowedHosts)
    {
        var overrides = new Dictionary<string, string>();
        if (allowedHosts != null)
        {
            overrides["AllowedHosts"] = allowedHosts;
        }

        return new BaGetApplication(_output, configOverrides: overrides);
    }

    /// <summary>
    /// The same application, with the host filtering that the default builder adds taken
    /// away again, so that the only such middleware left is the one BaGet puts in its own
    /// pipeline.
    /// </summary>
    /// <remarks>
    /// The builder contributes its host filtering through an <see cref="IStartupFilter"/>
    /// whose type is internal to ASP.NET Core, so the registration is matched by name.
    /// Hosting the application on a builder without the defaults, which is what this test
    /// used to do, is not open to it under the minimal hosting model: the pipeline is
    /// built by the entry point, and a bare host builder would not run it at all.
    /// </remarks>
    private static BaGetApplication WithoutTheBuildersHostFiltering(
        ITestOutputHelper output,
        Dictionary<string, string>? configOverrides)
    {
        return new BaGetApplication(
            output,
            configOverrides: configOverrides,
            configureServices: services =>
            {
                var hostFiltering = services
                    .Where(service =>
                        service.ServiceType == typeof(IStartupFilter) &&
                        service.ImplementationType?.Name == "HostFilteringStartupFilter")
                    .ToList();

                Assert.NotEmpty(hostFiltering);

                foreach (var service in hostFiltering)
                {
                    services.Remove(service);
                }
            });
    }
}