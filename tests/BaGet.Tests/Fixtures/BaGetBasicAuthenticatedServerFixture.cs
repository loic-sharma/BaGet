using System.Net;
using System.Diagnostics;

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






    //public abstract class AuthenticatedNugetClientWorkflowTest : IDisposable
    //{
    //    protected readonly ITestOutputHelper Helper;
    //    private readonly TestServer _server;
    //    protected readonly HttpClient HttpClient;

    //    public AuthenticatedNugetClientWorkflowTest(ITestOutputHelper helper)
    //    {
    //        Helper = helper ?? throw new ArgumentNullException(nameof(helper));
    //        _server = TestServerBuilder.Create().TraceToTestOutputHelper(Helper, LogLevel.Error).Build();
    //        var innerHandler = _server.CreateHandler();
    //        var authHandler = new NuGetLikeAuthenticationHandler(innerHandler);
    //        HttpClient = new HttpClient(authHandler);
    //    }



    //    //public async void dummytest()
    //    //{
    //    //    var response = await _server.CreateClient().GetAsync(string.Format("", ""));
    //    //}


    //    public void Dispose()
    //    {
    //        _server?.Dispose();
    //    }
    //}
}
