using System.Linq;
using System.Threading.Tasks;
using BaGet.Protocol.Internal;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Protocol.Tests
{
    public class SearchClientTests : IClassFixture<ProtocolFixture>
    {
        private readonly SearchClient _target;

        public SearchClientTests(ProtocolFixture fixture)
        {
            _target = fixture.SearchClient;
        }

        [Fact]
        public async Task GetsNewtonsoftJsonSearchResults()
        {
            var registrationurl = "https://api.nuget.org/v3/registration3-gz-semver2/newtonsoft.json/index.json";

            var result = await _target.SearchAsync("Newtonsoft");

            Assert.True(result.TotalHits > 0);
            Assert.True(result.Data.Count > 0);
            Assert.Equal(registrationurl, result.Data[0].RegistrationIndexUrl);
            Assert.Equal("Newtonsoft.Json", result.Data[0].PackageId);

            var expectedVersion = new NuGetVersion("12.0.2");
            var expectedResult = result.Data[0].Versions.SingleOrDefault(v => v.Version == expectedVersion);

            Assert.NotNull(expectedResult);
        }

        [Fact]
        public async Task GetsNewtonsoftJsonAutocompleteResults()
        {
            var result = await _target.AutocompleteAsync("newt");

            Assert.True(result.TotalHits > 0);
            Assert.True(result.Data.Count > 0);
            Assert.Contains(result.Data, id => id == "Newtonsoft.Json");
        }
    }
}
