using System.Threading.Tasks;
using BaGet.Protocol.Internal;
using Xunit;

namespace BaGet.Protocol.Tests
{
    public class RawSearchClientTests : IClassFixture<ProtocolFixture>
    {
        private readonly RawSearchClient _target;

        public RawSearchClientTests(ProtocolFixture fixture)
        {
            _target = fixture.SearchClient;
        }

        [Fact]
        public async Task GetDefaultSearchResults()
        {
            var response = await _target.SearchAsync();

            Assert.NotNull(response);
            Assert.Equal(1, response.TotalHits);

            var result = Assert.Single(response.Data);
            Assert.Equal("Test.Package", result.PackageId);
            Assert.Equal("Package Authors", Assert.Single(result.Authors));
            Assert.Equal(TestData.RegistrationIndexInlinedItemsUrl, result.RegistrationIndexUrl);
        }

        [Fact]
        public async Task GetDefaultAutocompleteResults()
        {
            var response = await _target.AutocompleteAsync();

            Assert.NotNull(response);
            Assert.Equal(1, response.TotalHits);
            Assert.Equal("Test.Package", Assert.Single(response.Data));
        }
    }
}
