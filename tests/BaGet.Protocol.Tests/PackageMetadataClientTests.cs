using System.Threading.Tasks;
using BaGet.Protocol.Internal;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Protocol.Tests
{
    public class PackageMetadataClientTests : IClassFixture<ProtocolFixture>
    {
        private readonly PackageMetadataClient _target;

        public PackageMetadataClientTests(ProtocolFixture fixture)
        {
            _target = fixture.MetadataClient;
        }

        [Fact]
        public async Task GetRegistrationIndexInlinedItems()
        {
            var result = await _target.GetRegistrationIndexOrNullAsync("Test.Package");

            Assert.NotNull(result);
            Assert.Equal(2, result.Pages.Count);

            Assert.True(result.Pages[0].Count == 1);
            Assert.True(result.Pages[0].ItemsOrNull.Count == 1);
            Assert.Equal("1.0.0", result.Pages[0].Lower);
            Assert.Equal("1.0.0", result.Pages[0].Upper);
            Assert.StartsWith(TestData.RegistrationIndexInlinedItemsUrl, result.Pages[0].RegistrationPageUrl);

            Assert.True(result.Pages[1].Count == 2);
            Assert.True(result.Pages[1].ItemsOrNull.Count == 2);
            Assert.Equal("2.0.0", result.Pages[1].Lower);
            Assert.Equal("3.0.0", result.Pages[1].Upper);
            Assert.StartsWith(TestData.RegistrationIndexInlinedItemsUrl, result.Pages[1].RegistrationPageUrl);
        }

        [Fact]
        public async Task GetRegistrationIndexPagedItems()
        {
            var result = await _target.GetRegistrationIndexOrNullAsync("Paged.Package");

            Assert.NotNull(result);
            Assert.Equal(2, result.Pages.Count);

            Assert.True(result.Pages[0].Count == 1);
            Assert.Null(result.Pages[0].ItemsOrNull);
            Assert.Equal("1.0.0", result.Pages[0].Lower);
            Assert.Equal("1.0.0", result.Pages[0].Upper);

            Assert.True(result.Pages[1].Count == 2);
            Assert.Null(result.Pages[1].ItemsOrNull);
            Assert.Equal("2.0.0", result.Pages[1].Lower);
            Assert.Equal("3.0.0", result.Pages[1].Upper);
        }

        [Fact]
        public async Task GetRegistrationPage()
        {
            var lower = NuGetVersion.Parse("2.0.0+build");
            var upper = NuGetVersion.Parse("3.0.0");
            var result = await _target.GetRegistrationPageOrNullAsync("Paged.Package", lower, upper);

            // TODO
        }

        [Fact]
        public async Task GetsRegistrationLeaf()
        {
            var version = NuGetVersion.Parse("1.0.0");
            var result = await _target.GetRegistrationLeafOrNullAsync("Test.Package", version);

            // TODO
        }

        [Fact]
        public async Task GetsRegistrationLeafUnlisted()
        {
            var version = NuGetVersion.Parse("2.0.0+build");
            var result = await _target.GetRegistrationLeafOrNullAsync("Paged.Package", version);

            // TODO
        }
    }
}
