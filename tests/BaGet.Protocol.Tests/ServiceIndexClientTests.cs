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

            Assert.Equal("3.0.0", result.Version);
            Assert.Equal(5, result.Resources.Count);

            Assert.Equal(TestData.CatalogIndexUrl, result.GetCatalogResourceUrl());
            Assert.Equal(TestData.PackageMetadataUrl, result.GetPackageMetadataResourceUrl());
            Assert.Equal(TestData.PackageContentUrl, result.GetPackageContentResourceUrl());
            Assert.Equal(TestData.SearchUrl, result.GetSearchQueryResourceUrl());
            Assert.Equal(TestData.AutocompleteUrl, result.GetSearchAutocompleteResourceUrl());
        }
    }
}
