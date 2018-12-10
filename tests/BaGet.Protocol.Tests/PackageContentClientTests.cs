using System;
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
            var httpClient = new HttpClient();
            _target = new PackageContentClient(httpClient);
        }

        [Fact]
        public async Task GetsPackageVersions()
        {
            var result = await _target.GetPackageVersionsOrNullAsync("https://api.nuget.org/v3-flatcontainer/newtonsoft.json/index.json");

            Assert.NotNull(result);
            Assert.NotEmpty(result.Versions);
        }

        [Fact]
        public async Task ReturnsNullIfPackageDoesNotExist()
        {
            var url = $"https://api.nuget.org/v3-flatcontainer/{Guid.NewGuid()}/index.json";

            Assert.Null(await _target.GetPackageVersionsOrNullAsync(url));
        }
    }
}
