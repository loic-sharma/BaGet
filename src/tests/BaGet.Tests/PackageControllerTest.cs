using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
namespace BaGet.Tests
{

    public class PackageControllerTest 
    {
        protected readonly ITestOutputHelper Helper;

        readonly string IndexUrlFormatString = "v3/package/{0}/index.json";

        public PackageControllerTest(ITestOutputHelper helper)
        {
            Helper = helper ?? throw new ArgumentNullException(nameof(helper));
        }

        [Theory]
        [InlineData("id01")]
        [InlineData("id02")]
        public async Task AskEmptyServerForNotExistingPackageID(string packageID)
        {
            using (TestServer server = TestServerBuilder.Create().UseEmptyTempFolder().TraceToTestOutputHelper(Helper,LogLevel.Error).Build())
            {
                //Ask Empty Storage for a not existings ID
                var response = await  server.CreateClient().GetAsync(string.Format(IndexUrlFormatString, packageID));
                Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
            }
        }
    }
}
