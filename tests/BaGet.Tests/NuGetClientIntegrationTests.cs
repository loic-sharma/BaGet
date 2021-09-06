using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Xunit;
using Xunit.Abstractions;

namespace BaGet.Tests
{
    /// <summary>
    /// Uses the official NuGet client to interact with the BaGet test host.
    /// </summary>
    public class NuGetClientIntegrationTests : IDisposable
    {
        private readonly BaGetApplication _app;
        private readonly HttpClient _client;

        private readonly Stream _packageStream;

        private readonly SourceRepository _repository;
        private readonly SourceCacheContext _cache;
        private readonly NuGet.Common.ILogger _logger;
        private readonly CancellationToken _cancellationToken;

        public NuGetClientIntegrationTests(ITestOutputHelper output)
        {
            _app = new BaGetApplication(output);
            _client = _app.CreateDefaultClient();
            _packageStream = TestResources.GetResourceStream(TestResources.Package);

            var sourceUri = new Uri(_app.Server.BaseAddress, "v3/index.json");
            var packageSource = new PackageSource(sourceUri.AbsoluteUri);
            var providers = new List<Lazy<INuGetResourceProvider>>();

            providers.Add(new Lazy<INuGetResourceProvider>(() => new HttpSourceResourceProviderTestHost(_client)));
            providers.AddRange(Repository.Provider.GetCoreV3());

            _repository = new SourceRepository(packageSource, providers);
            _cache = new SourceCacheContext { NoCache = true, MaxAge = new DateTimeOffset(), DirectDownload = true };
            _logger = NuGet.Common.NullLogger.Instance;
            _cancellationToken = CancellationToken.None;
        }

        [Fact]
        public async Task ValidIndex()
        {
            var index = await _repository.GetResourceAsync<ServiceIndexResourceV3>();

            Assert.Equal(12, index.Entries.Count);

            Assert.NotEmpty(index.GetServiceEntries("PackageBaseAddress/3.0.0"));
            Assert.NotEmpty(index.GetServiceEntries("PackagePublish/2.0.0"));
            Assert.NotEmpty(index.GetServiceEntries("RegistrationsBaseUrl"));
            Assert.NotEmpty(index.GetServiceEntries("SearchAutocompleteService"));
            Assert.NotEmpty(index.GetServiceEntries("SearchQueryService"));
            Assert.NotEmpty(index.GetServiceEntries("SymbolPackagePublish/4.9.0"));
        }

        [Fact]
        public async Task SearchReturnsResults()
        {
            await _app.AddPackageAsync(_packageStream);

            var resource = await _repository.GetResourceAsync<PackageSearchResource>();
            var searchFilter = new SearchFilter(includePrerelease: true);

            var results = await resource.SearchAsync(
                "",
                searchFilter,
                skip: 0,
                take: 20,
                _logger,
                _cancellationToken);

            var result = Assert.Single(results);

            Assert.Equal("TestData", result.Identity.Id);
            Assert.Equal("1.2.3", result.Identity.Version.ToNormalizedString());
            Assert.Equal("Test description", result.Description);
            Assert.Equal("Test author", result.Authors);
            Assert.Equal(0, result.DownloadCount);

            var versions = await result.GetVersionsAsync();
            var version = Assert.Single(versions);

            Assert.Equal("1.2.3", version.Version.ToNormalizedString());
            Assert.Equal(0, version.DownloadCount);
        }

        [Fact]
        public async Task SearchReturnsEmpty()
        {
            var resource = await _repository.GetResourceAsync<PackageSearchResource>();
            var searchFilter = new SearchFilter(includePrerelease: true);

            var results = await resource.SearchAsync(
                "PackageDoesNotExist",
                searchFilter,
                skip: 0,
                take: 20,
                _logger,
                _cancellationToken);

            Assert.Empty(results);
        }

        [Fact]
        public async Task AutocompleteReturnsResults()
        {
            await _app.AddPackageAsync(_packageStream);

            var resource = await _repository.GetResourceAsync<AutoCompleteResource>();
            var results = await resource.IdStartsWith(
                "",
                includePrerelease: true,
                _logger,
                _cancellationToken);

            var result = Assert.Single(results);

            Assert.Equal("TestData", result);
        }

        [Fact]
        public async Task AutocompleteReturnsEmpty()
        {
            var resource = await _repository.GetResourceAsync<AutoCompleteResource>();
            var results = await resource.IdStartsWith(
                "PackageDoesNotExist",
                includePrerelease: true,
                _logger,
                _cancellationToken);

            Assert.Empty(results);
        }

        [Fact]
        public async Task VersionListReturnsResults()
        {
            await _app.AddPackageAsync(_packageStream);

            var resource = await _repository.GetResourceAsync<FindPackageByIdResource>();
            var versions = await resource.GetAllVersionsAsync(
                "TestData",
                _cache,
                _logger,
                _cancellationToken);

            var version = Assert.Single(versions);

            Assert.Equal("1.2.3", version.ToNormalizedString());
        }

        [Fact]
        public async Task VersionListReturnsEmpty()
        {
            var resource = await _repository.GetResourceAsync<FindPackageByIdResource>();
            var versions = await resource.GetAllVersionsAsync(
                "PackageDoesNotExist",
                _cache,
                _logger,
                _cancellationToken);

            Assert.Empty(versions);
        }

        [Theory]
        [InlineData("TestData", "1.0.0", false)]
        [InlineData("TestData", "1.2.3", true)]
        [InlineData("PackageDoesNotExists", "1.0.0", false)]
        public async Task PackageExistsWorks(string packageId, string packageVersion, bool exists)
        {
            await _app.AddPackageAsync(_packageStream);

            var version = NuGetVersion.Parse(packageVersion);
            var resource = await _repository.GetResourceAsync<FindPackageByIdResource>();
            var result = await resource.DoesPackageExistAsync(
                packageId,
                version,
                _cache,
                _logger,
                _cancellationToken);

            Assert.Equal(exists, result);
        }

        [Theory]
        [InlineData("TestData", "1.0.0", false)]
        [InlineData("TestData", "1.2.3", true)]
        [InlineData("PackageDoesNotExists", "1.0.0", false)]
        public async Task PackageDownloadWorks(string packageId, string packageVersion, bool exists)
        {
            await _app.AddPackageAsync(_packageStream);

            using var packageStream = new MemoryStream();

            var version = NuGetVersion.Parse(packageVersion);
            var resource = await _repository.GetResourceAsync<FindPackageByIdResource>();
            var result = await resource.CopyNupkgToStreamAsync(
                packageId,
                version,
                packageStream,
                _cache,
                _logger,
                _cancellationToken);

            packageStream.Position = 0;

            Assert.Equal(exists, result);
            Assert.Equal(exists, packageStream.Length > 0);
        }

        [Fact]
        public async Task PackageMetadataReturnsOk()
        {
            await _app.AddPackageAsync(_packageStream);

            var resource = await _repository.GetResourceAsync<PackageMetadataResource>();
            var packages = await resource.GetMetadataAsync(
                "TestData",
                includePrerelease: true,
                includeUnlisted: true,
                _cache,
                _logger,
                _cancellationToken);

            var package = Assert.Single(packages);

            Assert.Equal("TestData", package.Identity.Id);
            Assert.Equal("1.2.3", package.Identity.Version.ToNormalizedString());
            Assert.Equal("Test description", package.Description);
            Assert.Equal("Test author", package.Authors);
            Assert.True(package.IsListed);
        }

        [Fact]
        public async Task PackageMetadataReturnsEmty()
        {
            var resource = await _repository.GetResourceAsync<PackageMetadataResource>();
            var packages = await resource.GetMetadataAsync(
                "PackageDoesNotExist",
                includePrerelease: true,
                includeUnlisted: true,
                _cache,
                _logger,
                _cancellationToken);

            Assert.Empty(packages);
        }

        public void Dispose()
        {
            _app.Dispose();
            _client.Dispose();
            _cache.Dispose();
        }
    }
}
