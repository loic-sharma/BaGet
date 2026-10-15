using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace BaGet.Tests
{
    /// <summary>
    /// A reverse proxy may either forward the path base to BaGet, or strip it before
    /// forwarding. The URLs BaGet advertises must include the configured path base
    /// either way, otherwise clients are sent to addresses that bypass the proxy.
    /// </summary>
    public class PathBaseIntegrationTests : IDisposable
    {
        private readonly BaGetApplication _app;
        private readonly HttpClient _client;

        public PathBaseIntegrationTests(ITestOutputHelper output)
        {
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
            // The proxy rewrote "/nuget/v3/index.json" to "/v3/index.json".
            using var response = await _client.GetAsync("v3/index.json");

            response.EnsureSuccessStatusCode();
            var content = JObject.Parse(await response.Content.ReadAsStringAsync());

            foreach (var resource in content["resources"])
            {
                var id = resource["@id"].Value<string>();
                Assert.StartsWith("http://localhost/nuget/", id);
            }
        }

        [Fact]
        public async Task ServiceIndexUsesPathBaseWhenTheProxyForwardsIt()
        {
            // The proxy forwarded the path base untouched.
            using var response = await _client.GetAsync("nuget/v3/index.json");

            response.EnsureSuccessStatusCode();
            var content = JObject.Parse(await response.Content.ReadAsStringAsync());

            foreach (var resource in content["resources"])
            {
                var id = resource["@id"].Value<string>();
                Assert.StartsWith("http://localhost/nuget/", id);
            }
        }

        public void Dispose()
        {
            _client?.Dispose();
            _app?.Dispose();
        }
    }
}
