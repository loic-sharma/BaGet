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

namespace BaGet.Tests;

/// <summary>
/// Uses the official NuGet client to interact with the BaGet test host.
/// </summary>
/// <summary>
/// Abstract: runs against the default repository at <c>/</c> (<see cref="NuGetClientIntegrationTests"/>) and a second one at
/// <c>/r/second/</c> (<see cref="NuGetClientIntegrationTestsSecondRepository"/>); the tests are identical.
/// </summary>
public abstract class NuGetClientIntegrationTestsBase : IDisposable
{
    private readonly string? _repositoryName;
    private readonly string _prefix;

    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    private readonly Stream? _packageStream;

    private readonly SourceRepository _repository;
    private readonly SourceCacheContext _cache;
    private readonly NuGet.Common.ILogger _logger;
    private readonly CancellationToken _cancellationToken;

    protected NuGetClientIntegrationTestsBase(ITestOutputHelper output, string? repository)
    {
        _repositoryName = repository;
        _prefix = repository == null ? "" : $"r/{repository}/";
        _app = new BaGetApplication(output);
        if (repository != null)
        {
            _app.CreateRepositoryAsync(repository).GetAwaiter().GetResult();
        }
        _client = _app.CreateDefaultClient();
        _packageStream = TestResources.GetResourceStream(TestResources.Package);

        var sourceUri = new Uri(_app.Server.BaseAddress, _prefix + "v3/index.json");
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
        var cancellationToken = TestContext.Current.CancellationToken;
        var index = await _repository.GetResourceAsync<ServiceIndexResourceV3>(cancellationToken);

        #pragma warning disable CS8602
        // The package-page templates (3 entries) point at the web UI, which exists in every repository.
        Assert.Equal(17, index.Entries.Count);
        #pragma warning restore CS8602

        Assert.NotEmpty(index.GetServiceEntries("PackageBaseAddress/3.0.0"));
        Assert.NotEmpty(index.GetServiceEntries("PackagePublish/2.0.0"));
        Assert.NotEmpty(index.GetServiceEntries("RegistrationsBaseUrl"));
        Assert.NotEmpty(index.GetServiceEntries("SearchAutocompleteService"));
        Assert.NotEmpty(index.GetServiceEntries("SearchQueryService"));
        Assert.NotEmpty(index.GetServiceEntries("SymbolPackagePublish/4.9.0"));
        Assert.NotEmpty(index.GetServiceEntries("PackageDetailsUriTemplate/5.1.0"));
        Assert.NotEmpty(index.GetServiceEntries("ReportAbuseUriTemplate/3.0.0-rc"));
    }

    [Fact]
    public async Task SearchReturnsResults()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repositoryName);

        var resource = await _repository.GetResourceAsync<PackageSearchResource>(cancellationToken);
        var searchFilter = new SearchFilter(includePrerelease: true);

        #pragma warning disable CS8602
        var results = await resource.SearchAsync(
            "",
            searchFilter,
            skip: 0,
            take: 20,
            _logger,
            _cancellationToken);
        #pragma warning restore CS8602

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
        var cancellationToken = TestContext.Current.CancellationToken;
        var resource = await _repository.GetResourceAsync<PackageSearchResource>(cancellationToken);
        var searchFilter = new SearchFilter(includePrerelease: true);

        #pragma warning disable CS8602
        var results = await resource.SearchAsync(
            "PackageDoesNotExist",
            searchFilter,
            skip: 0,
            take: 20,
            _logger,
            _cancellationToken);
        #pragma warning restore CS8602

        Assert.Empty(results);
    }

    [Fact]
    public async Task AutocompleteReturnsResults()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repositoryName);

        var resource = await _repository.GetResourceAsync<AutoCompleteResource>(cancellationToken);
        #pragma warning disable CS8602
        var results = await resource.IdStartsWith(
            "",
            includePrerelease: true,
            _logger,
            _cancellationToken);
        #pragma warning restore CS8602

        var result = Assert.Single(results);

        Assert.Equal("TestData", result);
    }

    [Fact]
    public async Task AutocompleteReturnsEmpty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var resource = await _repository.GetResourceAsync<AutoCompleteResource>(cancellationToken);
        #pragma warning disable CS8602
        var results = await resource.IdStartsWith(
            "PackageDoesNotExist",
            includePrerelease: true,
            _logger,
            _cancellationToken);
        #pragma warning restore CS8602

        Assert.Empty(results);
    }

    [Fact]
    public async Task VersionListReturnsResults()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repositoryName);

        var resource = await _repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
        #pragma warning disable CS8602
        var versions = await resource.GetAllVersionsAsync(
            "TestData",
            _cache,
            _logger,
            _cancellationToken);
        #pragma warning restore CS8602

        var version = Assert.Single(versions);

        Assert.Equal("1.2.3", version.ToNormalizedString());
    }

    [Fact]
    public async Task VersionListReturnsEmpty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var resource = await _repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
        #pragma warning disable CS8602
        var versions = await resource.GetAllVersionsAsync(
            "PackageDoesNotExist",
            _cache,
            _logger,
            _cancellationToken);
        #pragma warning restore CS8602

        Assert.Empty(versions);
    }

    [Theory]
    [InlineData("TestData", "1.0.0", false)]
    [InlineData("TestData", "1.2.3", true)]
    [InlineData("PackageDoesNotExists", "1.0.0", false)]
    public async Task PackageExistsWorks(string packageId, string packageVersion, bool exists)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repositoryName);

        var version = NuGetVersion.Parse(packageVersion);
        var resource = await _repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
        #pragma warning disable CS8602
        var result = await resource.DoesPackageExistAsync(
            packageId,
            version,
            _cache,
            _logger,
            _cancellationToken);
        #pragma warning restore CS8602

        Assert.Equal(exists, result);
    }

    [Theory]
    [InlineData("TestData", "1.0.0", false)]
    [InlineData("TestData", "1.2.3", true)]
    [InlineData("PackageDoesNotExists", "1.0.0", false)]
    public async Task PackageDownloadWorks(string packageId, string packageVersion, bool exists)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repositoryName);

        using var packageStream = new MemoryStream();

        var version = NuGetVersion.Parse(packageVersion);
        var resource = await _repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
        #pragma warning disable CS8602
        var result = await resource.CopyNupkgToStreamAsync(
            packageId,
            version,
            packageStream,
            _cache,
            _logger,
            _cancellationToken);
        #pragma warning restore CS8602

        packageStream.Position = 0;

        Assert.Equal(exists, result);
        Assert.Equal(exists, packageStream.Length > 0);
    }

    [Fact]
    public async Task PackageMetadataReturnsOk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repositoryName);

        var resource = await _repository.GetResourceAsync<PackageMetadataResource>(cancellationToken);
        #pragma warning disable CS8602
        var packages = await resource.GetMetadataAsync(
            "TestData",
            includePrerelease: true,
            includeUnlisted: true,
            _cache,
            _logger,
            _cancellationToken);
        #pragma warning restore CS8602

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
        var cancellationToken = TestContext.Current.CancellationToken;
        var resource = await _repository.GetResourceAsync<PackageMetadataResource>(cancellationToken);
        #pragma warning disable CS8602
        var packages = await resource.GetMetadataAsync(
            "PackageDoesNotExist",
            includePrerelease: true,
            includeUnlisted: true,
            _cache,
            _logger,
            _cancellationToken);
        #pragma warning restore CS8602

        Assert.Empty(packages);
    }

    public void Dispose()
    {
        _app.Dispose();
        _client.Dispose();
        _cache.Dispose();
    }
}

public class NuGetClientIntegrationTests(ITestOutputHelper output) : NuGetClientIntegrationTestsBase(output, null);

public class NuGetClientIntegrationTestsSecondRepository(ITestOutputHelper output) : NuGetClientIntegrationTestsBase(output, "second");
