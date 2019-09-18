using System;
using System.Threading.Tasks;
using BaGet.Protocol.Internal;
using Xunit;

namespace BaGet.Protocol.Tests
{
    public class RawPackageContentTests : IClassFixture<ProtocolFixture>
    {
        private readonly RawPackageContentClient _target;

        public RawPackageContentTests(ProtocolFixture fixture)
        {
            _target = fixture.ContentClient;
        }

        [Fact]
        public async Task GetsPackageVersions()
        {
            var result = await _target.GetPackageVersionsOrNullAsync("Test.Package");

            Assert.NotNull(result);
            Assert.Equal(2, result.Versions.Count);
            Assert.Equal("1.0.0", result.Versions[0]);
            Assert.Equal("2.0.0", result.Versions[1]);
        }

        [Fact]
        public async Task ReturnsNullIfPackageDoesNotExist()
        {
            var result = await _target.GetPackageVersionsOrNullAsync(Guid.NewGuid().ToString());

            Assert.Null(result);
        }

        // TODO: Test package download
        // TODO: Test package manifest download
    }
}
