using System.Threading.Tasks;
using BaGet.Protocol.Internal;
using Xunit;

namespace BaGet.Protocol.Tests
{
    public class ServiceIndexClientTests : IClassFixture<ProtocolFixture>
    {
        private readonly ServiceIndexClient _target;

        public ServiceIndexClientTests(ProtocolFixture fixture)
        {
            _target = fixture.ServiceIndexClient;
        }

        [Fact]
        public async Task GetsServiceIndex()
        {
            var result = await _target.GetAsync();

            Assert.Equal("3.0.0", result.Version.ToFullString());
        }
    }
}
