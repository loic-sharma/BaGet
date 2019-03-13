using System;
using System.Diagnostics;
using System.Net.Http;
using BaGet.Tests.Support;
using Microsoft.AspNetCore.TestHost;

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
}
