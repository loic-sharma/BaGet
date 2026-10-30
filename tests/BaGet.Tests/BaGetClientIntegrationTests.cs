using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using BaGet.Protocol;
using BaGet.Protocol.Models;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// Uses BaGet's client SDK to interact with the BaGet test host.
/// </summary>
/// <summary>
/// Abstract: runs against the default repository at <c>/</c> (<see cref="BaGetClientIntegrationTests"/>) and a second one at
/// <c>/r/second/</c> (<see cref="BaGetClientIntegrationTestsSecondRepository"/>); the tests are identical.
/// </summary>
public abstract class BaGetClientIntegrationTestsBase : IDisposable
{
    private readonly string? _repository;
    private readonly string _prefix;

    private readonly BaGetApplication _app;
    private readonly HttpClient _httpClient;
    private readonly NuGetClientFactory _clientFactory;
    private readonly NuGetClient _client;

    private readonly Stream? _packageStream;

    protected BaGetClientIntegrationTestsBase(ITestOutputHelper output, string? repository)
    {
        _repository = repository;
        _prefix = repository == null ? "" : $"r/{repository}/";
        _app = new BaGetApplication(output);
        if (repository != null)
        {
            _app.CreateRepositoryAsync(repository).GetAwaiter().GetResult();
        }

        var serviceIndexUrl = new Uri(_app.Server.BaseAddress, _prefix + "v3/index.json");

        _httpClient = _app.CreateClient();
        _clientFactory = new NuGetClientFactory(_httpClient, serviceIndexUrl.AbsoluteUri);
        _client = new NuGetClient(_clientFactory);

        _packageStream = TestResources.GetResourceStream(TestResources.Package);
    }

    [Fact]
    public async Task ValidIndex()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = _clientFactory.CreateServiceIndexClient();
        var index = await client.GetAsync(cancellationToken);

        #pragma warning disable CS8602
        Assert.Equal("3.0.0", index.Version);
        // The package-page templates (3 entries) point at the web UI, which exists in every repository.
        Assert.Equal(17, index.Resources.Count);
        #pragma warning restore CS8602

        #pragma warning disable CS8604
        Assert.NotEmpty(index.GetResourceUrl(new[] { "PackageBaseAddress/3.0.0" }));
        Assert.NotEmpty(index.GetResourceUrl(new[] { "PackagePublish/2.0.0" }));
        Assert.NotEmpty(index.GetResourceUrl(new[] { "RegistrationsBaseUrl" }));
        Assert.NotEmpty(index.GetResourceUrl(new[] { "SearchAutocompleteService" }));
        Assert.NotEmpty(index.GetResourceUrl(new[] { "SearchQueryService" }));
        Assert.NotEmpty(index.GetResourceUrl(new[] { "PackageDetailsUriTemplate/5.1.0" }));
        Assert.NotEmpty(index.GetResourceUrl(new[] { "ReportAbuseUriTemplate/3.0.0-rc" }));
        Assert.NotEmpty(index.GetResourceUrl(new[] { "SymbolPackagePublish/4.9.0" }));
        #pragma warning restore CS8604
    }

    [Fact]
    public async Task SearchReturnsResults()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repository);

        var results = await _client.SearchAsync(cancellationToken: cancellationToken);

        #pragma warning disable CS8604
        var result = Assert.Single(results);
        var author = Assert.Single(result.Authors);
        var version = Assert.Single(result.Versions);
        #pragma warning restore CS8604

        Assert.Equal("TestData", result.PackageId);
        Assert.Equal("1.2.3", result.Version);
        Assert.Equal("Test description", result.Description);
        Assert.Equal("Test author", author);
        Assert.Equal(0, result.TotalDownloads);

        Assert.Equal("1.2.3", version.Version);
        Assert.Equal(0, version.Downloads);
    }

    [Fact]
    public async Task SearchReturnsEmpty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var results = await _client.SearchAsync("PackageDoesNotExist", cancellationToken);

        #pragma warning disable CS8604
        Assert.Empty(results);
        #pragma warning restore CS8604
    }

    [Fact]
    public async Task AutocompleteReturnsResults()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repository);

        #pragma warning disable CS8602
        var results = await _client.AutocompleteAsync(cancellationToken: cancellationToken);
        #pragma warning restore CS8602

        #pragma warning disable CS8604
        var result = Assert.Single(results);
        #pragma warning restore CS8604

        Assert.Equal("TestData", result);
    }

    [Fact]
    public async Task AutocompleteReturnsEmpty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        #pragma warning disable CS8602
        var results = await _client.AutocompleteAsync("PackageDoesNotExist", cancellationToken);
        #pragma warning restore CS8602

        #pragma warning disable CS8604
        Assert.Empty(results);
        #pragma warning restore CS8604
    }

    [Fact]
    public async Task AutocompleteVersions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repository);

        var client = _clientFactory.CreateAutocompleteClient();
        var results = await client.ListPackageVersionsAsync("TestData", cancellationToken: cancellationToken);

        #pragma warning disable CS8604
        var result = Assert.Single(results.Data);
        #pragma warning restore CS8604

        Assert.Equal(1, results.TotalHits);
        Assert.Equal("1.2.3", result);
    }

    [Fact]
    public async Task AutocompleteVersionsReturnsEmpty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = _clientFactory.CreateAutocompleteClient();
        var results = await client.ListPackageVersionsAsync("PackageDoesNotExist", cancellationToken: cancellationToken);

        #pragma warning disable CS8604
        Assert.Empty(results.Data);
        #pragma warning restore CS8604
        Assert.Equal(0, results.TotalHits);
    }

    [Fact]
    public async Task VersionListReturnsResults()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repository);

        var versions = await _client.ListPackageVersionsAsync("TestData", cancellationToken);

        var version = Assert.Single(versions);

        Assert.Equal("1.2.3", version.ToNormalizedString());
    }

    [Fact]
    public async Task VersionListReturnsEmpty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var versions = await _client.ListPackageVersionsAsync("PackageDoesNotExist", cancellationToken);

        Assert.Empty(versions);
    }

    [Theory]
    [InlineData("TestData", "1.0.0", false)]
    [InlineData("TestData", "1.2.3", true)]
    [InlineData("PackageDoesNotExists", "1.0.0", false)]
    public async Task PackageDownloadWorks(string? packageId, string packageVersion, bool exists)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repository);

        try
        {
            var version = NuGetVersion.Parse(packageVersion);

            using var memoryStream = new MemoryStream();
            using var packageStream = await _client.DownloadPackageAsync(packageId, version, cancellationToken);

            await packageStream.CopyToAsync(memoryStream, cancellationToken);
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
    [InlineData("TestData", "1.0.0", false)]
    [InlineData("TestData", "1.2.3", true)]
    [InlineData("PackageDoesNotExists", "1.0.0", false)]
    public async Task ManifestDownloadWorks(string packageId, string packageVersion, bool exists)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repository);

        try
        {
            var version = NuGetVersion.Parse(packageVersion);

            using var memoryStream = new MemoryStream();
            using var packageStream = await _client.DownloadPackageManifestAsync(packageId, version, cancellationToken);

            await packageStream.CopyToAsync(memoryStream, cancellationToken);
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
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repository);

        var packages = await _client.GetPackageMetadataAsync("TestData", cancellationToken);

        var package = Assert.Single(packages);

        Assert.Equal("TestData", package.PackageId);
        Assert.Equal("1.2.3", package.Version);
        Assert.Equal("Test description", package.Description);
        Assert.Equal("Test author", package.Authors);
        Assert.True(package.Listed);
    }

    [Fact]
    public async Task PackageMetadataReturnsEmty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var packages = await _client.GetPackageMetadataAsync("PackageDoesNotExist", cancellationToken);

        Assert.Empty(packages);
    }

    public void Dispose()
    {
        _app.Dispose();
        _httpClient.Dispose();
    }
}

public class BaGetClientIntegrationTests(ITestOutputHelper output) : BaGetClientIntegrationTestsBase(output, null);

public class BaGetClientIntegrationTestsSecondRepository(ITestOutputHelper output) : BaGetClientIntegrationTestsBase(output, "second");
