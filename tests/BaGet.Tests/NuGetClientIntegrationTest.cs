using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using BaGet.Tests.Support;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using Xunit;
using Xunit.Abstractions;

namespace BaGet.Tests
{
    /// <summary>
    /// Uses official nuget client packages to talk to test host.
    /// </summary>
    public class NuGetClientIntegrationTest : IDisposable
    {
        protected readonly ITestOutputHelper Helper;
        private readonly TestServer server;
        private readonly string IndexUrlString = "v3/index.json";
        private SourceRepository _sourceRepository;
        private readonly SourceCacheContext _cacheContext;
        private HttpSourceResource _httpSource;
        private HttpClient _httpClient;
        private readonly string indexUrl;

        public NuGetClientIntegrationTest(ITestOutputHelper helper)
        {
            Helper = helper ?? throw new ArgumentNullException(nameof(helper));
            server = TestServerBuilder.Create().TraceToTestOutputHelper(Helper, LogLevel.Error).Build();
            var providers = new List<Lazy<INuGetResourceProvider>>();
            providers.AddRange(Repository.Provider.GetCoreV3());
            providers.Add(new Lazy<INuGetResourceProvider>(() => new PackageMetadataResourceV3Provider()));
            _httpClient = server.CreateClient();
            providers.Insert(0, new Lazy<INuGetResourceProvider>(() => new HttpSourceResourceProviderTestHost(_httpClient)));

            indexUrl = new Uri(server.BaseAddress, IndexUrlString).AbsoluteUri;
            var packageSource = new PackageSource(indexUrl);
            _sourceRepository = new SourceRepository(packageSource, providers);
            _cacheContext = new SourceCacheContext() { NoCache = true, MaxAge = new DateTimeOffset(), DirectDownload = true };
            _httpSource = _sourceRepository.GetResource<HttpSourceResource>();
            Assert.IsType<HttpSourceTestHost>(_httpSource.HttpSource);
        }
        public void Dispose()
        {
            server?.Dispose();
        }

        [Fact]
        public async Task GetIndexShouldReturn200()
        {
            var index = await _httpClient.GetAsync(indexUrl);
            Assert.Equal(HttpStatusCode.OK, index.StatusCode);
            return;
        }

        [Fact]
        public async Task IndexResourceHasManyEntries()
        {
            var indexResource = await _sourceRepository.GetResourceAsync<ServiceIndexResourceV3>();
            Assert.NotEmpty(indexResource.Entries);
        }

        [Fact]
        public async Task IndexIncludesAtLeastOneSearchQueryEntry()
        {
            var indexResource = await _sourceRepository.GetResourceAsync<ServiceIndexResourceV3>();
            Assert.NotEmpty(indexResource.GetServiceEntries("SearchQueryService"));
        }

        [Fact]
        public async Task IndexIncludesAtLeastOneRegistrationsBaseEntry()
        {
            var indexResource = await _sourceRepository.GetResourceAsync<ServiceIndexResourceV3>();
            Assert.NotEmpty(indexResource.GetServiceEntries("RegistrationsBaseUrl"));
        }

        [Fact]
        public async Task IndexIncludesAtLeastOnePackageBaseAddressEntry()
        {
            var indexResource = await _sourceRepository.GetResourceAsync<ServiceIndexResourceV3>();
            Assert.NotEmpty(indexResource.GetServiceEntries("PackageBaseAddress/3.0.0"));
        }

        [Fact]
        public async Task IndexIncludesAtLeastOneSearchAutocompleteServiceEntry()
        {
            var indexResource = await _sourceRepository.GetResourceAsync<ServiceIndexResourceV3>();
            Assert.NotEmpty(indexResource.GetServiceEntries("SearchAutocompleteService"));
        }
    }
}
