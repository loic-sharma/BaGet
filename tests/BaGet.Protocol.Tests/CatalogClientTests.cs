using System;
using System.Linq;
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

        [Fact]
        public async Task GetsCatalogLeaf()
        {
            var index = await _target.GetIndexAsync();
            var pageItem = index.Items.First();

            var page = await _target.GetPageAsync(pageItem.Url);
            var leafItem = page.Items.First();

            CatalogLeaf result;
            switch (leafItem.Type)
            {
                case CatalogLeafType.PackageDelete:
                    result = await _target.GetPackageDeleteLeafAsync(leafItem.CatalogLeafUrl);
                    break;

                case CatalogLeafType.PackageDetails:
                    result = await _target.GetPackageDetailsLeafAsync(leafItem.CatalogLeafUrl);
                    break;

                default:
                    throw new NotSupportedException($"Unknown leaf type '{leafItem.Type}'");
            }

            Assert.NotNull(result.PackageId);
        }
    }
}
