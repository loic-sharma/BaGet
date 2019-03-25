using System;
using System.Diagnostics;
using System.Net.Http;
using BaGet.Tests.Support;
using Microsoft.AspNetCore.TestHost;
using Xunit;
using Xunit.Abstractions;
namespace BaGet.Tests.Fixtures
{
    public abstract class BaGetServerFixture : IDisposable
    {
        public HttpClient HttpClient => _httpClient.Value;
        private TestServer _server = null;
        private NuGetLikeAuthenticationHandler _handler = null;
        private readonly Lazy<HttpClient> _httpClient;

        protected virtual void OnAfterNuGetLikeAuthenticationHandlerCreated(NuGetLikeAuthenticationHandler handler)
        { }

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
