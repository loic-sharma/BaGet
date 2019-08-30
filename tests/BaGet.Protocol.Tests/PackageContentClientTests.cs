using System;
using System.Threading.Tasks;
using BaGet.Protocol.Internal;
using Xunit;

namespace BaGet.Protocol.Tests
{
    public class PackageContentTests : IClassFixture<ProtocolFixture>
    {
        private readonly PackageContentClient _target;

        public PackageContentTests(ProtocolFixture fixture)
        {
            _target = fixture.ContentClient;
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
