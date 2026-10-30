using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Protocol.Tests;

public class NuGetClientFactoryTests : IClassFixture<ProtocolFixture>
{
    private readonly NuGetClientFactory _target;

    public NuGetClientFactoryTests(ProtocolFixture fixture)
    {
        _target = fixture.NuGetClientFactory;
    }

    [Fact]
    public async Task DownloadsPackage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = _target.CreatePackageContentClient();

        using (var stream = await client.DownloadPackageOrNullAsync(
                   "Test.Package",
                   NuGetVersion.Parse("1.0.0"), cancellationToken))
        {
            Assert.NotNull(stream);

            using (var reader = new StreamReader(stream))
            {
                Assert.Equal(TestData.PackageContentNupkg, await reader.ReadToEndAsync(cancellationToken));
            }
        }
    }

    [Fact]
    public async Task DownloadsPackageManifest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = _target.CreatePackageContentClient();

        using (var stream = await client.DownloadPackageManifestOrNullAsync(
                   "Test.Package",
                   NuGetVersion.Parse("1.0.0"), cancellationToken))
        {
            Assert.NotNull(stream);

            using (var reader = new StreamReader(stream))
            {
                Assert.Equal(TestData.PackageContentNuspec, await reader.ReadToEndAsync(cancellationToken));
            }
        }
    }

    [Fact]
    public async Task ListsPackageVersions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = _target.CreatePackageContentClient();

        var response = await client.GetPackageVersionsOrNullAsync("Test.Package", cancellationToken);

        Assert.NotNull(response);
        #pragma warning disable CS8604
        Assert.Equal(new[] { "1.0.0", "2.0.0" }, response.Versions.ToArray());
        #pragma warning restore CS8604
    }

    [Fact]
    public async Task ListsPackageVersionsOfPagedPackage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = _target.CreatePackageContentClient();

        var response = await client.GetPackageVersionsOrNullAsync("Paged.Package", cancellationToken);

        Assert.NotNull(response);
        #pragma warning disable CS8604
        Assert.Equal(new[] { "1.0.0", "2.0.0", "3.0.0" }, response.Versions.ToArray());
        #pragma warning restore CS8604
    }

    [Fact]
    public async Task ListsAllPackageVersions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = _target.CreateAutocompleteClient();

        var response = await client.ListPackageVersionsAsync("Test.Package", cancellationToken: cancellationToken);

        Assert.NotNull(response);
        Assert.Equal(3, response.TotalHits);
        #pragma warning disable CS8604
        Assert.Equal(new[] { "1.0.0", "2.0.0", "3.0.0" }, response.Data.ToArray());
        #pragma warning restore CS8604
    }

    [Fact]
    public async Task GetsPackageMetadataInlined()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = _target.CreatePackageMetadataClient();

        var response = await client.GetRegistrationIndexOrNullAsync("Test.Package", cancellationToken);

        Assert.NotNull(response);
        #pragma warning disable CS8602
        Assert.Equal(2, response.Pages.Count);
        #pragma warning restore CS8602
        Assert.All(response.Pages, page => Assert.NotNull(page.ItemsOrNull));
        #pragma warning disable CS8602, CS8603
        Assert.Equal(
            new[] { "1.0.0", "2.0.0+build", "3.0.0" },
            response.Pages
                .SelectMany(p => p.ItemsOrNull)
                .Select(i => i.PackageMetadata.Version)
                .ToArray());
        #pragma warning restore CS8602, CS8603
    }

    [Fact]
    public async Task GetsPackageMetadataPaged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = _target.CreatePackageMetadataClient();

        var response = await client.GetRegistrationIndexOrNullAsync("Paged.Package", cancellationToken);

        Assert.NotNull(response);
        #pragma warning disable CS8602
        Assert.Equal(2, response.Pages.Count);
        #pragma warning restore CS8602
        Assert.All(response.Pages, page => Assert.Null(page.ItemsOrNull));
        Assert.Equal(
            new[] { TestData.RegistrationPageFirstUrl, TestData.RegistrationPageUrl },
            response.Pages.Select(p => p.RegistrationPageUrl).ToArray());
    }

    [Fact]
    public async Task GetsSinglePackageMetadataInlined()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = _target.CreatePackageMetadataClient();

        var response = await client.GetRegistrationLeafAsync(TestData.RegistrationLeafListedUrl, cancellationToken);

        Assert.NotNull(response);
        Assert.True(response.Listed);
        Assert.Equal(
            "https://test.example/v3/content/test.package/1.0.0/test.package.1.0.0.nupkg",
            response.PackageContentUrl);
    }

    [Fact]
    public async Task GetsSinglePackageMetadataPaged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = _target.CreatePackageMetadataClient();

        var response = await client.GetRegistrationPageAsync(TestData.RegistrationPageUrl, cancellationToken);

        Assert.NotNull(response);
        Assert.NotNull(response.ItemsOrNull);

        var metadata = response.ItemsOrNull[1].PackageMetadata;

        #pragma warning disable CS8602
        Assert.Equal("Paged.Package", metadata.PackageId);
        #pragma warning restore CS8602
        Assert.Equal("3.0.0", metadata.Version);
    }

    [Fact]
    public async Task Searches()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = _target.CreateSearchClient();

        var response = await client.SearchAsync(cancellationToken: cancellationToken);

        Assert.NotNull(response);
        Assert.Equal(1, response.TotalHits);
        #pragma warning disable CS8604
        Assert.Equal("Test.Package", Assert.Single(response.Data).PackageId);
        #pragma warning restore CS8604
    }

    [Fact]
    public async Task Autocompletes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = _target.CreateAutocompleteClient();

        var response = await client.AutocompleteAsync(cancellationToken: cancellationToken);

        Assert.NotNull(response);
        Assert.Equal(1, response.TotalHits);
        #pragma warning disable CS8604
        Assert.Equal("Test.Package", Assert.Single(response.Data));
        #pragma warning restore CS8604
    }

    [Fact]
    public async Task ResolvesResourcesFromTheServiceIndex()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = _target.CreateServiceIndexClient();

        var serviceIndex = await client.GetAsync(cancellationToken);

        Assert.Equal("https://test.example/v3/content", serviceIndex.GetPackageContentResourceUrl());
        Assert.Equal("https://test.example/v3/metadata", serviceIndex.GetPackageMetadataResourceUrl());
        Assert.Equal(TestData.SearchUrl, serviceIndex.GetSearchQueryResourceUrl());
        Assert.Equal(TestData.AutocompleteUrl, serviceIndex.GetSearchAutocompleteResourceUrl());
        Assert.Equal(TestData.CatalogIndexUrl, serviceIndex.GetCatalogResourceUrl());
    }

    [Fact]
    public async Task FetchesTheServiceIndexOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new CountingHttpMessageHandler(new TestDataHttpMessageHandler());
        var target = new NuGetClientFactory(new HttpClient(handler), TestData.ServiceIndexUrl);

        await target.CreatePackageContentClient().GetPackageVersionsOrNullAsync("Test.Package", cancellationToken);
        await target.CreateSearchClient().SearchAsync(cancellationToken: cancellationToken);
        await target.CreateAutocompleteClient().AutocompleteAsync(cancellationToken: cancellationToken);

        Assert.Equal(1, handler.RequestCount(TestData.ServiceIndexUrl));
        Assert.Equal(1, handler.RequestCount(TestData.PackageContentVersionListUrl));
        Assert.Equal(1, handler.RequestCount(TestData.DefaultSearchUrl));
        Assert.Equal(1, handler.RequestCount(TestData.DefaultAutocompleteUrl));
    }

    [Fact]
    public async Task ReturnsNullWhenPackageDoesNotExist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var contentClient = _target.CreatePackageContentClient();
        var metadataClient = _target.CreatePackageMetadataClient();
        var packageId = Guid.NewGuid().ToString();

        Assert.Null(await contentClient.GetPackageVersionsOrNullAsync(packageId, cancellationToken));
        Assert.Null(await metadataClient.GetRegistrationIndexOrNullAsync(packageId, cancellationToken));
    }

    [Fact]
    public void ThrowsWhenConstructedWithoutHttpClient()
    {
        Assert.Throws<ArgumentNullException>(
            () => new NuGetClientFactory(null, TestData.ServiceIndexUrl));
        Assert.Throws<ArgumentNullException>(
            () => new NuGetClientFactory(new HttpClient(), null));
    }
}