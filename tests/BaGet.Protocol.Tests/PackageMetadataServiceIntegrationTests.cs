using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Protocol.Tests
{
    public class PackageMetadataServiceIntegrationTests
    {
        private readonly PackageMetadataService _target;

        public PackageMetadataServiceIntegrationTests()
        {
            var httpClient = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            });

            var serviceIndexClient = new ServiceIndexClient(httpClient);
            var registrationClient = new RegistrationClient(httpClient);
            var packageContentClient = new PackageContentClient(httpClient);

            var serviceIndex = "https://api.nuget.org/v3/index.json";
            var serviceIndexService = new ServiceIndexService(serviceIndex, serviceIndexClient);

            _target = new PackageMetadataService(
                serviceIndexService,
                registrationClient,
                packageContentClient);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GetsAllVersionsForNewtonsoftJson(bool includeUnlisted)
        {
            var result = await _target.GetAllVersionsOrNullAsync("Newtonsoft.Json", includeUnlisted);

            Assert.NotEmpty(result);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GetsAllVersionsForFake(bool includeUnlisted)
        {
            var result = await _target.GetAllVersionsOrNullAsync("Fake", includeUnlisted);
        }
    }
}
