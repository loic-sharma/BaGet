using BaGet.Tests.Abstractions;
using BaGet.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace BaGet.Tests
{

    public class BasicAuthenticatedPackageControllerTest : PackageControllerTest, IClassFixture<BaGetBasicAuthenticatedServerFixture>
    {
        public BasicAuthenticatedPackageControllerTest(ITestOutputHelper helper, BaGetBasicAuthenticatedServerFixture fixture) : base(helper, fixture)
        {
            fixture.AllowedUserCredentials = new System.Net.NetworkCredential("myName", "myPassword", "myDomain");
        }
    }
}
