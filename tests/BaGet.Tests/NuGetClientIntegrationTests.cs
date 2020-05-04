using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
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
    public class NuGetClientIntegrationTests : IClassFixture<BaGetWebApplicationFactory>
    {
        private readonly WebApplicationFactory<Startup> _factory;
        private readonly HttpClient _client;

        private readonly SourceRepository _sourceRepository;
        private readonly SourceCacheContext _cacheContext;

        public NuGetClientIntegrationTests(
            BaGetWebApplicationFactory factory,
            ITestOutputHelper output)
        {
            _factory = factory.WithOutput(output);
            _client = _factory.CreateDefaultClient();

            var sourceUri = new Uri(_factory.Server.BaseAddress, "v3/index.json");
            var packageSource = new PackageSource(sourceUri.AbsoluteUri);
            var providers = new List<Lazy<INuGetResourceProvider>>();

            providers.Add(new Lazy<INuGetResourceProvider>(() => new HttpSourceResourceProviderTestHost(_client)));
            providers.AddRange(Repository.Provider.GetCoreV3());

            _sourceRepository = new SourceRepository(packageSource, providers);
            _cacheContext = new SourceCacheContext() { NoCache = true, MaxAge = new DateTimeOffset(), DirectDownload = true };
        }

        [Fact]
        public async Task ValidIndex()
        {
            var index = await _sourceRepository.GetResourceAsync<ServiceIndexResourceV3>();

            Assert.Equal(12, index.Entries.Count);

            Assert.NotEmpty(index.GetServiceEntries("PackageBaseAddress/3.0.0"));
            Assert.NotEmpty(index.GetServiceEntries("PackagePublish/2.0.0"));
            Assert.NotEmpty(index.GetServiceEntries("RegistrationsBaseUrl"));
            Assert.NotEmpty(index.GetServiceEntries("SearchAutocompleteService"));
            Assert.NotEmpty(index.GetServiceEntries("SearchQueryService"));
            Assert.NotEmpty(index.GetServiceEntries("SymbolPackagePublish/4.9.0"));
        }
    }
}
