using System;
using System.IO;
using System.Threading.Tasks;
using BaGet.Protocol;
using BaGet.Protocol.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using NuGet.Versioning;
using Xunit;
using Xunit.Abstractions;

namespace BaGet.Tests
{
    /// <summary>
    /// Uses BaGet's client SDK to interact with the BaGet test host.
    /// </summary>
    public class BaGetClientIntegrationTests : IClassFixture<BaGetWebApplicationFactory>
    {
        private readonly WebApplicationFactory<Startup> _factory;
        private readonly NuGetClientFactory _clientFactory;
        private readonly NuGetClient _client;

        public BaGetClientIntegrationTests(
            BaGetWebApplicationFactory factory,
            ITestOutputHelper output)
        {
            _factory = factory.WithOutput(output);

            var serviceIndexUrl = new Uri(_factory.Server.BaseAddress, "v3/index.json");

            var httpClient = _factory.CreateDefaultClient();

            _clientFactory = new NuGetClientFactory(httpClient, serviceIndexUrl.AbsoluteUri);
            _client = new NuGetClient(_clientFactory);
        }

        [Fact]
        public async Task ValidIndex()
        {
            var client = _clientFactory.CreateServiceIndexClient();
            var index = await client.GetAsync();

            Assert.Equal("3.0.0", index.Version);
            Assert.Equal(12, index.Resources.Count);

            Assert.NotEmpty(index.GetResourceUrl(new[] { "PackageBaseAddress/3.0.0" }));
            Assert.NotEmpty(index.GetResourceUrl(new[] { "PackagePublish/2.0.0" }));
            Assert.NotEmpty(index.GetResourceUrl(new[] { "RegistrationsBaseUrl" }));
            Assert.NotEmpty(index.GetResourceUrl(new[] { "SearchAutocompleteService" }));
            Assert.NotEmpty(index.GetResourceUrl(new[] { "SearchQueryService" }));
            Assert.NotEmpty(index.GetResourceUrl(new[] { "SymbolPackagePublish/4.9.0" }));
        }

        [Fact]
        public async Task SearchReturnsResults()
        {
            await _factory.AddPackageAsync(PackageData.Default);

            var results = await _client.SearchAsync();

            var result = Assert.Single(results);
            var author = Assert.Single(result.Authors);
            var version = Assert.Single(result.Versions);

            Assert.Equal("DefaultPackage", result.PackageId);
            Assert.Equal("1.2.3", result.Version);
            Assert.Equal("Default package description", result.Description);
            Assert.Equal("Default package author", author);
            Assert.Equal(0, result.TotalDownloads);

            Assert.Equal("1.2.3", version.Version);
            Assert.Equal(0, version.Downloads);
        }

        [Fact]
        public async Task SearchReturnsEmpty()
        {
            var results = await _client.SearchAsync("PackageDoesNotExist");

            Assert.Empty(results);
        }

        [Fact]
        public async Task AutocompleteReturnsResults()
        {
            await _factory.AddPackageAsync(PackageData.Default);

            var results = await _client.AutocompleteAsync();

            var result = Assert.Single(results);

            Assert.Equal("DefaultPackage", result);
        }

        [Fact]
        public async Task AutocompleteReturnsEmpty()
        {
            var results = await _client.AutocompleteAsync("PackageDoesNotExist");

            Assert.Empty(results);
        }

        [Fact]
        public async Task VersionListReturnsResults()
        {
            await _factory.AddPackageAsync(PackageData.Default);

            var versions = await _client.ListPackageVersionsAsync("DefaultPackage");

            var version = Assert.Single(versions);

            Assert.Equal("1.2.3", version.ToNormalizedString());
        }

        [Fact]
        public async Task VersionListReturnsEmpty()
        {
            var versions = await _client.ListPackageVersionsAsync("PackageDoesNotExist");

            Assert.Empty(versions);
        }

        [Theory]
        [InlineData("DefaultPackage", "1.0.0", false)]
        [InlineData("DefaultPackage", "1.2.3", true)]
        [InlineData("PackageDoesNotExists", "1.0.0", false)]
        public async Task PackageDownloadWorks(string packageId, string packageVersion, bool exists)
        {
            await _factory.AddPackageAsync(PackageData.Default);

            try
            {
                var version = NuGetVersion.Parse(packageVersion);

                using var memoryStream = new MemoryStream();
                using var packageStream = await _client.DownloadPackageAsync(packageId, version);

                await packageStream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                Assert.True(exists);
                Assert.Equal(exists, memoryStream.Length > 0);
            }
            catch (PackageNotFoundException)
            {
                Assert.False(exists);
            }
        }

        [Theory]
        [InlineData("DefaultPackage", "1.0.0", false)]
        [InlineData("DefaultPackage", "1.2.3", true)]
        [InlineData("PackageDoesNotExists", "1.0.0", false)]
        public async Task ManifestDownloadWorks(string packageId, string packageVersion, bool exists)
        {
            await _factory.AddPackageAsync(PackageData.Default);

            try
            {
                var version = NuGetVersion.Parse(packageVersion);

                using var memoryStream = new MemoryStream();
                using var packageStream = await _client.DownloadPackageManifestAsync(packageId, version);

                await packageStream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                Assert.True(exists);
                Assert.Equal(exists, memoryStream.Length > 0);
            }
            catch (PackageNotFoundException)
            {
                Assert.False(exists);
            }
        }

        [Fact]
        public async Task PackageMetadataReturnsOk()
        {
            await _factory.AddPackageAsync(PackageData.Default);

            var packages = await _client.GetPackageMetadataAsync("DefaultPackage");

            var package = Assert.Single(packages);

            Assert.Equal("DefaultPackage", package.PackageId);
            Assert.Equal("1.2.3", package.Version);
            Assert.Equal("Default package description", package.Description);
            Assert.Equal("Default package author", package.Authors);
            Assert.True(package.Listed);
        }

        [Fact]
        public async Task PackageMetadataReturnsEmty()
        {
            var packages = await _client.GetPackageMetadataAsync("PackageDoesNotExist");

            Assert.Empty(packages);
        }
    }
}
