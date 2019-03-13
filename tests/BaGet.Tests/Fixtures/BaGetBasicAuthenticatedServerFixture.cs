using System.Net;
using System.Diagnostics;
using BaGet.Tests.Support;

namespace BaGet.Tests.Fixtures
{
    public class BaGetBasicAuthenticatedServerFixture : BaGetServerFixture
    {
        public NetworkCredential AllowedUserCredentials { get; set; }


        protected override void OnAfterNuGetLikeAuthenticationHandlerCreated(NuGetLikeAuthenticationHandler handler)
        {
            Debug.Assert(AllowedUserCredentials != null);
            handler.AllowedUser = AllowedUserCredentials;
        }
        protected override ITestServerBuilder GetBuilder()
        {
            return TestServerBuilder.Create().UseBasicAuthentication(AllowedUserCredentials, "myRealm");
        }
    }
}
