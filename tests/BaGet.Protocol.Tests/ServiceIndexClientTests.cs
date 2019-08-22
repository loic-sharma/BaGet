using System.Threading.Tasks;
using Xunit;

namespace BaGet.Protocol.Tests
{
    public class ServiceIndexClientTests
    {
        private readonly IServiceIndexResource _target;

        public ServiceIndexClientTests()
        {
            _target = new NuGetClient("https://api.nuget.org/v3/index.json")
                .CreateServiceIndexClient();
        }

        [Fact]
        public async Task GetsServiceIndex()
        {
            var result = await _target.GetAsync();

            Assert.Equal("3.0.0", result.Version.ToFullString());
        }
    }
}
