namespace BaGet.Tests.Fixtures
{
    public class BaGetTokenAuthenticatedServerFixture : BaGetServerFixture
    {
        protected override ITestServerBuilder GetBuilder()
        {
            return TestServerBuilder.Create().UseJwtBearerAuthentication();
        }
    }
}
