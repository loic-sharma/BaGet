using BaGet.Tests.Abstractions;
using BaGet.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace BaGet.Tests
{
    public class TokenAuthenticatedPackageContollerTest : PackageControllerTest, IClassFixture<BaGetTokenAuthenticatedServerFixture>
    {
        public TokenAuthenticatedPackageContollerTest(ITestOutputHelper helper, BaGetTokenAuthenticatedServerFixture fixture) : base(helper, fixture)
        {

        }
    }
}
