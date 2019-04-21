using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Protocol.Tests
{
    public class PackageMetadataClientTests
    {
        private readonly PackageMetadataClient _target;

        public static readonly NuGetVersion NewtonsoftJsonLowerVersion = NuGetVersion.Parse("3.5.8");
        public static readonly NuGetVersion NewtonsoftJsonUpperVersion = NuGetVersion.Parse("12.0.1-beta2");

        public static readonly NuGetVersion FakePage1LowerVersion = NuGetVersion.Parse("1.0.0-alpha-10");
        public static readonly NuGetVersion FakePage1UpperVersion = NuGetVersion.Parse("1.66.1");

        public PackageMetadataClientTests()
        {
            var httpClient = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            });

            var serviceIndex = new ServiceIndexClient(httpClient, "https://api.nuget.org/v3/index.json");
            var urlGeneratorFactory = new UrlGeneratorClientFactory(serviceIndex);

            _target = new PackageMetadataClient(urlGeneratorFactory, httpClient);
        }

        [Fact]
        public async Task GetsNewtonsoftJsonRegistrationIndex()
        {
            var result = await _target.GetRegistrationIndexOrNullAsync("Newtonsoft.Json");

            Assert.NotNull(result);
            Assert.Equal(2, result.Pages.Count);
            Assert.True(result.Pages[0].Count == 64);
            Assert.True(result.Pages[0].ItemsOrNull.Count == 64);
            Assert.Equal(NewtonsoftJsonLowerVersion, result.Pages[0].Lower);
            Assert.Equal(NewtonsoftJsonUpperVersion, result.Pages[0].Upper);
        }

        [Fact]
        public async Task GetsFakeRegistrationIndex()
        {
            // If this test breaks, "GetFakesRegistrationPage" will need to be updated.
            var result = await _target.GetRegistrationIndexOrNullAsync("FAKE");

            Assert.NotNull(result);
            Assert.True(result.Pages.Count >= 27);
            Assert.Null(result.Pages[0].ItemsOrNull);
            Assert.Equal(64, result.Pages[0].Count);
            Assert.Equal(FakePage1LowerVersion, result.Pages[0].Lower);
            Assert.Equal(FakePage1UpperVersion, result.Pages[0].Upper);
        }

        [Fact]
        public async Task GetsFakeRegistrationPage()
        {
            var result = await _target.GetRegistrationPageOrNullAsync("FAKE", FakePage1LowerVersion, FakePage1UpperVersion);

            Assert.NotNull(result);
            Assert.Equal(64, result.Count);
            Assert.Equal(new NuGetVersion("1.0.0-alpha-10"), result.Lower);
            Assert.Equal(new NuGetVersion("1.66.1"), result.Upper);
        }

        [Fact]
        public async Task GetsNewtonsoftRegistrationLeaf()
        {
            var leaf = await _target.GetRegistrationLeafOrNullAsync("Newtonsoft.Json", NewtonsoftJsonLowerVersion);

            Assert.NotNull(leaf);
        }

        [Fact]
        public async Task GetFakeRegistrationLeaf()
        {
            var leaf = await _target.GetRegistrationLeafOrNullAsync("FAKE", FakePage1LowerVersion);

            Assert.NotNull(leaf);
        }
    }
}
