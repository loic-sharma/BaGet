using System.Diagnostics;
using System.Net;
using BaGet.Tests.Support;

namespace BaGet.Tests.Fixtures
{
    public class BaGetBasicAuthenticatedServerFixture : BaGetServerFixture
    {
        public NetworkCredential AllowedUserCredentials { get; set; }
        public string Realm { get; set; }

        protected override void OnAfterNuGetLikeAuthenticationHandlerCreated(NuGetLikeAuthenticationHandler handler)
        {
            Debug.Assert(AllowedUserCredentials != null);
            handler.Credential = AllowedUserCredentials; //inject testcase credential into nuget client 
        }
        protected override ITestServerBuilder GetBuilder()
        {
            return TestServerBuilder.Create().UseBasicAuthentication(AllowedUserCredentials, Realm); //inject testcase credential into BaGet Server
        }
    }
}
