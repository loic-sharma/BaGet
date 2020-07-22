using System;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Protocol.Internal;
using BaGet.Protocol.Models;
using Xunit;

namespace BaGet.Protocol.Tests
{
    public class RawCatalogClientTests : IClassFixture<ProtocolFixture>
    {
        private readonly RawCatalogClient _target;

        public RawCatalogClientTests(ProtocolFixture fixture)
        {
            _target = fixture.CatalogClient;
        }

        [Fact]
        public async Task GetCatalogIndex()
        {
            var result = await _target.GetIndexAsync();

            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Equal(2, result.Items.Count);
            Assert.Equal(TestData.CatalogPageUrl, result.Items.Select(i => i.CatalogPageUrl).First());
        }

        [Fact]
        public async Task GetCatalogPage()
        {
            var page = await _target.GetPageAsync(TestData.CatalogPageUrl);

            Assert.Equal(2, page.Count);
            Assert.Equal(2, page.Items.Count);
            Assert.Equal(TestData.CatalogIndexUrl, page.CatalogIndexUrl);
            Assert.Equal(TestData.PackageDetailsCatalogLeafUrl, page.Items[0].CatalogLeafUrl);
            Assert.Equal(TestData.PackageDeleteCatalogLeafUrl, page.Items[1].CatalogLeafUrl);
        }

        [Fact]
        public async Task GetPackageDetailsLeaf()
        {
            var leaf = await _target.GetPackageDetailsLeafAsync(TestData.PackageDetailsCatalogLeafUrl);

            Assert.Equal(TestData.PackageDetailsCatalogLeafUrl, leaf.CatalogLeafUrl);
            Assert.Equal("PackageDetails", leaf.Type[0]);
            Assert.Equal("catalog:Permalink", leaf.Type[1]);

            Assert.Equal("Test.Package", leaf.PackageId);
            Assert.Equal("1.0.0", leaf.PackageVersion);
        }

        [Fact]
        public async Task GetPackageDeleteLeaf()
        {
            var leaf = await _target.GetPackageDeleteLeafAsync(TestData.PackageDeleteCatalogLeafUrl);

            Assert.Equal(TestData.PackageDeleteCatalogLeafUrl, leaf.CatalogLeafUrl);
            Assert.Equal("PackageDelete", leaf.Type[0]);
            Assert.Equal("catalog:Permalink", leaf.Type[1]);

            Assert.Equal("Deleted.Package", leaf.PackageId);
            Assert.Equal("1.0.0", leaf.PackageVersion);
        }

        [Fact]
        public async Task ThrowsOnTypeMismatch()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => _target.GetPackageDetailsLeafAsync(TestData.PackageDeleteCatalogLeafUrl));
            await Assert.ThrowsAsync<ArgumentException>(() => _target.GetPackageDeleteLeafAsync(TestData.PackageDetailsCatalogLeafUrl));
        }
    }
}
