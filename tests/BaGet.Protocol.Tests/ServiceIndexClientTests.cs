using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Protocol.Tests
{
    public class ServiceIndexClientTests
    {
        private readonly ServiceIndexClient _target;

        public ServiceIndexClientTests()
        {
            var httpClient = new HttpClient();
            _target = new ServiceIndexClient(httpClient);
        }

        [Fact]
        public async Task GetsServiceIndex()
        {
            var result = await _target.GetServiceIndexAsync("https://api.nuget.org/v3/index.json");
        }
    }
}
