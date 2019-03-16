using BaGet.Tests.Abstractions;
using BaGet.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace BaGet.Tests
{

    public class AnonymousAuthenticatedPackageControllerTest : PackageControllerTest, IClassFixture<BaGetAnonymousAuthenticatedServerFixture>
    {
        public AnonymousAuthenticatedPackageControllerTest(ITestOutputHelper helper, BaGetAnonymousAuthenticatedServerFixture fixture) : base(helper, fixture)
        {
        }
    }
}
