using System;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Protocol.Tests
{
    public class PackageContentTests
    {
        private readonly IPackageContentResource _target;

        public PackageContentTests()
        {
            _target = new NuGetClient("https://api.nuget.org/v3/index.json")
                .CreatePackageContentClient();
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
