using System;
using System.Threading.Tasks;
using BaGet.Protocol;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace BaGet.Tests
{
    public class RegistrationIndexControllerTest
    {
        protected readonly ITestOutputHelper Helper;

        private readonly string RegistrationUrlFormatString = "v3/registration/{0}/index.json";

        public RegistrationIndexControllerTest(ITestOutputHelper helper)
        {
            Helper = helper ?? throw new ArgumentNullException(nameof(helper));
        }


        [Fact]
        public async Task AskEmptyServerForNewtonsoftJsonRegistration()
        {
            using (var server = TestServerBuilder.Create().TraceToTestOutputHelper(Helper, LogLevel.Error).Build())
            {
                var client = new RegistrationClient(server.CreateClient());
                var url = string.Format(RegistrationUrlFormatString, "newtonsoft.json");
                var result = await client.GetRegistrationIndexOrNullAsync(url);
                Assert.Null(result);
            }
        }
    }
}
