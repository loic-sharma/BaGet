using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// The V2 (OData) feed. These drive the HTTP surface a V2 client actually calls -
/// see also <see cref="NuGetV2ClientIntegrationTests"/>, which drives NuGet's own
/// V2 client against it.
/// </summary>
/// <summary>
/// Abstract: runs against the default repository at <c>/</c> (<see cref="V2FeedIntegrationTests"/>) and a second one at
/// <c>/r/second/</c> (<see cref="V2FeedIntegrationTestsSecondRepository"/>); the tests are identical.
/// </summary>
public abstract class V2FeedIntegrationTestsBase : IDisposable
{
    private readonly string? _repository;
    private readonly string _prefix;

    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace DataServices = "http://schemas.microsoft.com/ado/2007/08/dataservices";

    private readonly BaGetApplication _app;
    private readonly HttpClient _client;
    private readonly Stream? _packageStream;

    protected V2FeedIntegrationTestsBase(ITestOutputHelper output, string? repository)
    {
        _repository = repository;
        _prefix = repository == null ? "" : $"r/{repository}/";
        _app = new BaGetApplication(output);
        if (repository != null)
        {
            _app.CreateRepositoryAsync(repository).GetAwaiter().GetResult();
        }
        _client = _app.CreateDefaultClient();
        _client.BaseAddress = new Uri(_client.BaseAddress!, _prefix);
        _packageStream = TestResources.GetResourceStream(TestResources.Package);
    }

    #pragma warning disable CS8602
    private static IEnumerable<XElement> Entries(string document)
        => XDocument.Parse(document).Root.Elements(Atom + "entry");
    #pragma warning restore CS8602

    private static string? PropertyOf(XElement entry, string name)
        => entry.Descendants(DataServices + name).FirstOrDefault()?.Value;

    [Fact]
    public async Task TheServiceDocumentAdvertisesThePackagesCollection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync("api/v2", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = XDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Contains(
            "Packages",
            document.Descendants()
                .Where(e => e.Name.LocalName == "collection")
                .Select(e => e.Attribute("href")?.Value));
    }

    [Fact]
    public async Task TheMetadataDocumentParsesAndDescribesTheEntity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync("api/v2/$metadata", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Parsing is the assertion: a malformed EDM breaks every V2 client.
        var document = XDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        var entityType = document.Descendants()
            .Single(e => e.Name.LocalName == "EntityType");
        #pragma warning disable CS8602
        Assert.Equal("V2FeedPackage", entityType.Attribute("Name").Value);
        #pragma warning restore CS8602

        #pragma warning disable CS8602
        var properties = entityType.Elements()
            .Where(e => e.Name.LocalName == "Property")
            .Select(e => e.Attribute("Name").Value)
            .ToList();
        #pragma warning restore CS8602

        Assert.Contains("Id", properties);
        Assert.Contains("Version", properties);
        Assert.Contains("IsLatestVersion", properties);
        Assert.Contains("Dependencies", properties);

        // EDMX served as XML, entries are media link entries, and the two service operations are declared with their parameters.
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Edmx", document.Root?.Name.LocalName);
        Assert.Equal("true", entityType.Attributes().Single(a => a.Name.LocalName == "HasStream").Value);

        #pragma warning disable CS8602
        var functions = document.Descendants()
            .Where(e => e.Name.LocalName == "FunctionImport")
            .ToDictionary(
                e => e.Attribute("Name").Value,
                e => e.Elements().Where(p => p.Name.LocalName == "Parameter").Select(p => p.Attribute("Name").Value).ToList());
        #pragma warning restore CS8602

        Assert.Equal(new[] { "searchTerm", "includePrerelease", "semVerLevel" }, functions["Search"]);
        Assert.Equal(new[] { "id", "semVerLevel" }, functions["FindPackagesById"]);
        Assert.All(
            document.Descendants().Where(e => e.Name.LocalName == "FunctionImport"),
            f =>
            {
                #pragma warning disable CS8602
                Assert.Equal("Collection(NuGetGallery.V2FeedPackage)", f.Attribute("ReturnType").Value);
                Assert.Equal("Packages", f.Attribute("EntitySet").Value);
                #pragma warning restore CS8602
            });
    }

    [Fact]
    public async Task FindPackagesByIdReturnsEveryVersion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repository);

        using var response = await _client.GetAsync("api/v2/FindPackagesById()?id='TestData'", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entries = Entries(await response.Content.ReadAsStringAsync(cancellationToken)).ToList();

        Assert.Single(entries);
        Assert.Equal("1.2.3", PropertyOf(entries[0], "NormalizedVersion"));
    }

    [Fact]
    public async Task FindPackagesByIdAcceptsAnUnquotedId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repository);

        using var response = await _client.GetAsync("api/v2/FindPackagesById()?id=TestData", cancellationToken);

        Assert.Single(Entries(await response.Content.ReadAsStringAsync(cancellationToken)));
    }

    [Fact]
    public async Task FindPackagesByIdIsEmptyForAnUnknownId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync("api/v2/FindPackagesById()?id='No.Such.Package'", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(Entries(await response.Content.ReadAsStringAsync(cancellationToken)));
    }

    [Fact]
    public async Task AnEntryPointsAtADownloadablePackage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repository);

        using var feed = await _client.GetAsync("api/v2/FindPackagesById()?id='TestData'", cancellationToken);
        var entry = Entries(await feed.Content.ReadAsStringAsync(cancellationToken)).Single();

        #pragma warning disable CS8602
        var source = entry.Element(Atom + "content").Attribute("src").Value;
        #pragma warning restore CS8602

        // The advertised content URL has to actually serve the nupkg.
        using var download = await _client.GetAsync(source, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.NotEmpty(await download.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task TheLatestVersionFlagsAreSet()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repository);

        using var response = await _client.GetAsync("api/v2/FindPackagesById()?id='TestData'", cancellationToken);
        var entry = Entries(await response.Content.ReadAsStringAsync(cancellationToken)).Single();

        // A single stable version is both the latest and the absolute latest.
        Assert.Equal("true", PropertyOf(entry, "IsLatestVersion"));
        Assert.Equal("true", PropertyOf(entry, "IsAbsoluteLatestVersion"));
        Assert.Equal("false", PropertyOf(entry, "IsPrerelease"));
    }

    [Fact]
    public async Task PackagesByKeyReturnsOneEntry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repository);

        using var response = await _client.GetAsync("api/v2/Packages(Id='TestData',Version='1.2.3')", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = XDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        #pragma warning disable CS8602
        Assert.Equal("entry", document.Root.Name.LocalName);
        #pragma warning restore CS8602
        Assert.Equal("1.2.3", PropertyOf(document.Root, "NormalizedVersion"));
    }

    [Fact]
    public async Task PackagesByKeyIsNotFoundForAnUnknownVersion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repository);

        using var response = await _client.GetAsync("api/v2/Packages(Id='TestData',Version='9.9.9')", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SearchFindsAPushedPackage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repository);

        using var response = await _client.GetAsync("api/v2/Search()?searchTerm=''&includePrerelease=false", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entries = Entries(await response.Content.ReadAsStringAsync(cancellationToken)).ToList();

        #pragma warning disable CS8602
        Assert.Contains("TestData", entries.Select(e => e.Element(Atom + "title").Value));
        #pragma warning restore CS8602
    }

    [Fact]
    public async Task PackagesListsWhatTheFeedHolds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repository);

        using var response = await _client.GetAsync("api/v2/Packages()", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(Entries(await response.Content.ReadAsStringAsync(cancellationToken)));
    }

    [Fact]
    public async Task TheFeedIsAtomEncoded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync("api/v2/Packages()", cancellationToken);

        #pragma warning disable CS8602
        Assert.Equal("application/atom+xml", response.Content.Headers.ContentType.MediaType);
        #pragma warning restore CS8602
    }

    public void Dispose()
    {
        _packageStream?.Dispose();
        _client?.Dispose();
        _app?.Dispose();
    }
}

public class V2FeedIntegrationTests(ITestOutputHelper output) : V2FeedIntegrationTestsBase(output, null);

public class V2FeedIntegrationTestsSecondRepository(ITestOutputHelper output) : V2FeedIntegrationTestsBase(output, "second");

/// <summary>
/// The V2 feed with "Feeds:EnableV2" switched off.
/// </summary>
public class V2FeedDisabledTests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public V2FeedDisabledTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string>
            {
                { "Feeds:EnableV2", "false" },
            });

        _client = _app.CreateDefaultClient();
    }

    [Theory]
    [InlineData("api/v2")]
    [InlineData("api/v2/$metadata")]
    [InlineData("api/v2/Packages()")]
    [InlineData("api/v2/FindPackagesById()?id='TestData'")]
    public async Task TheFeedIsNotFound(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync(path, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PushingStillWorks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Turning off the read feed must not turn off the publish endpoint, which
        // shares the "api/v2" prefix.
        using var stream = TestResources.GetResourceStream(TestResources.Package);

        await _app.AddPackageAsync(stream, cancellationToken);

        using var response = await _client.GetAsync("v3/registration/testdata/index.json", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public void Dispose()
    {
        _client?.Dispose();
        _app?.Dispose();
    }
}
