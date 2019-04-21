using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Protocol.Tests
{
    public class PackageContentTests
    {
        private readonly PackageContentClient _target;

        public PackageContentTests()
        {
            var httpClient = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            });

            var serviceIndex = new ServiceIndexClient(httpClient, "https://api.nuget.org/v3/index.json");
            var urlGeneratorFactory = new UrlGeneratorClientFactory(serviceIndex);

            _target = new PackageContentClient(urlGeneratorFactory, httpClient);
        }

        [Fact]
        public async Task GetsPackageVersions()
        {
            var result = await _target.GetPackageVersionsOrNullAsync("Newtonsoft.Json");

            Assert.NotNull(result);
            Assert.NotEmpty(result.Versions);
        }

        [Fact]
        public async Task ReturnsNullIfPackageDoesNotExist()
        {
            var result = await _target.GetPackageVersionsOrNullAsync(Guid.NewGuid().ToString());

            Assert.Null(result);
        }
    }
}
