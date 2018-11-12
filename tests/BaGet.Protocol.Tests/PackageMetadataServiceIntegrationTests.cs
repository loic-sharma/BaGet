using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Protocol.Tests
{
    public class PackageMetadataServiceTests
    {
        private readonly PackageMetadataService _target;

        public PackageMetadataServiceTests()
        {
            var httpClient = new HttpClient();
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
            var result = await _target.GetAllVersionsAsync("Newtonsoft.Json", includeUnlisted);

            Assert.NotEmpty(result);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GetsAllVersionsForFake(bool includeUnlisted)
        {
            var result = await _target.GetAllVersionsAsync("Fake", includeUnlisted);
        }
    }
}
