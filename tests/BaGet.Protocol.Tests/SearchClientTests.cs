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

            var result = await _target.SearchAsync(new SearchRequest { Query = "Newtonsoft" });

            Assert.True(result.TotalHits > 0);
            Assert.True(result.Data.Count > 0);
            Assert.Equal(registrationurl, result.Data[0].RegistrationIndexUrl);
            Assert.Equal("Newtonsoft.Json", result.Data[0].PackageId);
            Assert.Equal(new NuGetVersion("12.0.2"), result.Data[0].Version);
        }

        [Fact]
        public async Task GetsNewtonsoftJsonAutocompleteResults()
        {
            var result = await _target.AutocompleteAsync(new AutocompleteRequest { Query = "newt" });

            Assert.True(result.TotalHits > 0);
            Assert.True(result.Data.Count > 0);
            Assert.Contains(result.Data, id => id == "Newtonsoft.Json");
        }
    }
}
