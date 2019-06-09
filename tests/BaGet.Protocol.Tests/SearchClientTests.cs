using System.Net.Http;
using System.Threading.Tasks;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Protocol.Tests
{
    public class SearchClientTests
    {
        private readonly SearchClient _target;

        public SearchClientTests()
        {
            var httpClient = new HttpClient();
            _target = new SearchClient(httpClient);
        }

        [Fact]
        public async Task GetsNewtonsoftJsonSearchResults()
        {
            var searchQuery = "https://api-v2v3search-0.nuget.org/query?q=newtonsoft";
            var registrationurl = "https://api.nuget.org/v3/registration3/newtonsoft.json/index.json";

            var result = await _target.GetSearchResultsAsync(searchQuery);

            Assert.True(result.TotalHits > 0);
            Assert.True(result.Data.Count > 0);
            Assert.Equal(registrationurl, result.Data[0].RegistrationUrl);
            Assert.Equal("Newtonsoft.Json", result.Data[0].Id);
            Assert.Equal(new NuGetVersion("12.0.2"), result.Data[0].Version);
        }

        [Fact]
        public async Task GetsNewtonsoftJsonAutocompleteResults()
        {
            var query = "https://api-v2v3search-0.nuget.org/autocomplete?q=newt";
            
            var result = await _target.GetAutocompleteResultsAsync(query);

            Assert.True(result.TotalHits > 0);
            Assert.True(result.Data.Count > 0);
            Assert.Contains(result.Data, id => id == "Newtonsoft.Json");
        }
    }
}
