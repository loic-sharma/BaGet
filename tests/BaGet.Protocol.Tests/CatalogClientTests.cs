using System;
using System.Threading.Tasks;
using BaGet.Protocol.Internal;
using Xunit;

namespace BaGet.Protocol.Tests
{
    public class CatalogClientTests : IClassFixture<ProtocolFixture>
    {
        private readonly CatalogClient _target;

        public CatalogClientTests(ProtocolFixture fixture)
        {
            _target = fixture.CatalogClient;
        }

        [Fact]
        public async Task GetsCatalogIndex()
        {
            var result = await _target.GetIndexAsync();

            Assert.NotNull(result);
            Assert.True(result.Count > 0);
            Assert.NotEmpty(result.Items);
        }
    }
}
