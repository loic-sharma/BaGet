namespace BaGet.Tests.Fixtures
{
    public class BaGetAnonymousAuthenticatedServerFixture : BaGetServerFixture
    {
        protected override ITestServerBuilder GetBuilder()
        {
            return TestServerBuilder.Create();
        }
    }
}
