using System.Threading.Tasks;
using BaGet.Protocol.Internal;
using Xunit;

namespace BaGet.Protocol.Tests
{
    public class RawAutocompleteClientTests : IClassFixture<ProtocolFixture>
    {
        private readonly RawAutocompleteClient _target;

        public RawAutocompleteClientTests(ProtocolFixture fixture)
        {
            _target = fixture.AutocompleteClient;
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
