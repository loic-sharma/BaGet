using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Xunit.Abstractions;

namespace BaGet.Tests
{
    public class ApiTest : IClassFixture<BaGetWebApplicationFactory>
    {
        private readonly WebApplicationFactory<Startup> _factory;
        private readonly HttpClient _client;

        public ApiTest(BaGetWebApplicationFactory factory, ITestOutputHelper output)
        {
            _factory = factory.WithOutput(output);
            _client = _factory.CreateClient();
        }

        [Fact]
        public async Task IndexReturnsOk()
        {
            var response = await _client.GetAsync("v3/index.json");
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(TestData.ServiceIndex, content);
        }

        [Fact]
        public async Task VersionListReturnsNotFound()
        {
            var response = await _client.GetAsync("v3/package/PackageDoesNotExist/index.json");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
