using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Xunit.Abstractions;

namespace BaGet.Tests
{
    public class SpaIntegrationTests : IClassFixture<BaGetWebApplicationFactory>
    {
        private readonly WebApplicationFactory<Startup> _factory;
        private readonly HttpClient _client;

        public SpaIntegrationTests(BaGetWebApplicationFactory factory, ITestOutputHelper output)
        {
            var config = new Dictionary<string, string>
            {
                { "PathBase", "/baget" }
            };

            _factory = factory.WithOutputAndConfig(output, config);
            _client = _factory.CreateClient();
        }

        [Fact]
        public async Task IndexLinksReplaced()
        {
            var response = await _client.GetAsync("baget");
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.StartsWith("<!doctype html>", content);
            Assert.DoesNotContain("__BAGET_PLACEHOLDER_PATH_BASE__", content);
            Assert.EndsWith("</html>", content);

            // Find all href and src links to validate their path base
            var match = Regex.Match(content, "(href|src)=\"([^\"]*)\"");
            while (match.Success)
            {
                Assert.StartsWith("/baget/", match.Groups[2].Value);
                match = match.NextMatch();
            }
        }
    }
}
