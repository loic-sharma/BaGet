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

        [Fact]
        public async Task AddsAutocompleteParameters()
        {
            await Task.Yield();

            // TODO: Assert request URL query parameters.
            // var response = await _target.AutocompleteAsync(
            //     "query",
            //     skip: 2,
            //     take: 5,
            //     includePrerelease: false,
            //     includeSemVer2: false);
        }

        [Fact]
        public async Task ListsPackageVersions()
        {
            await Task.Yield();

            // var response = await _target.ListPackageVersionsAsync("PackageId");

            // Assert.NotNull(response);
            // Assert.Equal(1, response.TotalHits);
            // Assert.Equal("1.0.0", response.Data[0]);
        }

        [Fact]
        public async Task AddsListPackageVersionsParameters()
        {
            await Task.Yield();

            // TODO: Assert request URL query parameters.
            // var response = await _target.ListPackageVersionsAsync(
            //     "query",
            //     includePrerelease: false,
            //     includeSemVer2: false);
        }
    }
}
