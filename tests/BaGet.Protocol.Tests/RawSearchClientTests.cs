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

            var packageType = Assert.Single(result.PackageTypes);
            Assert.Equal("Dependency", packageType.Name);
        }

        [Fact]
        public async Task AddsParameters()
        {
            await Task.Yield();

            // TODO: Assert request URL query parameters.
            // var response = await _target.SearchAsync(
            //     "query",
            //     skip: 2,
            //     take: 5,
            //     includePrerelease: false,
            //     includeSemVer2: false);
        }
    }
}
