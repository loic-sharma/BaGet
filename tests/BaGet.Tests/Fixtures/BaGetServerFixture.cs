using System;
using System.Net.Http;
using Microsoft.AspNetCore.TestHost;
using System.Diagnostics;

namespace BaGet.Tests.Fixtures
{
    public abstract class BaGetServerFixture : IDisposable
    {

        protected virtual void OnAfterNuGetLikeAuthenticationHandlerCreated(NuGetLikeAuthenticationHandler handler)
        { }

        private readonly Lazy<HttpClient> _httpClient;//= new Lazy<HttpClient>(SomeClass.IOnlyWantToCallYouOnce);
        public HttpClient HttpClient => _httpClient.Value;
        private TestServer _server = null;
        private NuGetLikeAuthenticationHandler _handler = null;
        /// <summary>
        /// calledvia Lazy!
        /// </summary>
        /// <returns></returns>
        protected abstract ITestServerBuilder GetBuilder();

        private HttpClient CreateHttpClient()
        {
            Debug.Assert(_server == null);
            _server = GetBuilder().Build();
            var innerHandler = _server.CreateHandler();
            _handler = new NuGetLikeAuthenticationHandler(innerHandler);
            OnAfterNuGetLikeAuthenticationHandlerCreated(_handler);
            return new HttpClient(_handler) { BaseAddress = _server.BaseAddress };
        }

        public BaGetServerFixture()
        {
            _httpClient = new Lazy<HttpClient>(CreateHttpClient);
        }

        public void Dispose()
        {
            _server?.Dispose();
            if (_httpClient.IsValueCreated)
            {
                _httpClient.Value.Dispose();
            }
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
