using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.TestHost;
using Xunit;
using Xunit.Abstractions;
namespace BaGet.Tests
{
    public class SearchControllerTest
    {
        protected readonly ITestOutputHelper Helper;

        readonly string EntryUrl = "/v3/search"; 

        public SearchControllerTest(ITestOutputHelper helper)
        {
            Helper = helper ?? throw new ArgumentNullException(nameof(helper));
        }

        [Fact]
        public async Task AskEmptyServer()
        {
            using (TestServer server = TestServerBuilder.Create().Build())
            {
                var response = await server.CreateClient().GetAsync(EntryUrl);
                Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            }
        }

    }
}
