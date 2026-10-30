using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BaGet.Protocol.Internal;
using Xunit;

namespace BaGet.Protocol.Tests;

public class RawSearchClientTests : IClassFixture<ProtocolFixture>
{
    private readonly RawSearchClient _target;

    public RawSearchClientTests(ProtocolFixture fixture)
    {
        _target = fixture.SearchClient;
    }

    [Fact]
    public async Task GetDefaultSearchResults()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var response = await _target.SearchAsync(cancellationToken: cancellationToken);

        Assert.NotNull(response);
        Assert.Equal(1, response.TotalHits);

        #pragma warning disable CS8604
        var result = Assert.Single(response.Data);
        #pragma warning restore CS8604
        Assert.Equal("Test.Package", result.PackageId);
        #pragma warning disable CS8604
        Assert.Equal("Package Authors", Assert.Single(result.Authors));
        #pragma warning restore CS8604
        Assert.Equal(TestData.RegistrationIndexInlinedItemsUrl, result.RegistrationIndexUrl);

        #pragma warning disable CS8604
        var packageType = Assert.Single(result.PackageTypes);
        #pragma warning restore CS8604
        Assert.Equal("Dependency", packageType.Name);
    }

    [Fact]
    public async Task AddsDefaultParameters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new RecordingHttpMessageHandler(TestData.DefaultSearch);
        var target = new RawSearchClient(new HttpClient(handler), TestData.SearchUrl);

        await target.SearchAsync(cancellationToken: cancellationToken);

        var url = Assert.Single(handler.Requests);
        var query = TestQueryString.Parse(url);

        Assert.Equal("test.example", url.Host);
        Assert.Equal("/v3/search", url.AbsolutePath);
        Assert.Equal(
            new[] { "prerelease", "semVerLevel", "take" },
            query.Keys.OrderBy(k => k).ToArray());
        Assert.Equal("20", query["take"]);
        Assert.Equal("True", query["prerelease"]);
        Assert.Equal("2.0.0", query["semVerLevel"]);
    }

    [Fact]
    public async Task AddsParameters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new RecordingHttpMessageHandler(TestData.DefaultSearch);
        var target = new RawSearchClient(new HttpClient(handler), TestData.SearchUrl);

        await target.SearchAsync(
            "query",
            skip: 2,
            take: 5,
            includePrerelease: false,
            includeSemVer2: false, cancellationToken);

        var url = Assert.Single(handler.Requests);
        var query = TestQueryString.Parse(url);

        Assert.Equal("/v3/search", url.AbsolutePath);
        Assert.Equal(
            new[] { "q", "skip", "take" },
            query.Keys.OrderBy(k => k).ToArray());
        Assert.Equal("2", query["skip"]);
        Assert.Equal("5", query["take"]);
        Assert.Equal("query", query["q"]);
    }

    [Fact]
    public async Task EscapesTheQuery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new RecordingHttpMessageHandler(TestData.DefaultSearch);
        var target = new RawSearchClient(new HttpClient(handler), TestData.SearchUrl);

        await target.SearchAsync("packageid:Test.Package Foo&Bar", cancellationToken: cancellationToken);

        var url = Assert.Single(handler.Requests);
        var query = TestQueryString.Parse(url);

        Assert.DoesNotContain("Foo&Bar", url.AbsoluteUri);
        Assert.Equal("packageid:Test.Package Foo&Bar", query["q"]);
    }

    [Fact]
    public async Task SkipIsOmittedWhenZero()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new RecordingHttpMessageHandler(TestData.DefaultSearch);
        var target = new RawSearchClient(new HttpClient(handler), TestData.SearchUrl);

        await target.SearchAsync("query", skip: 0, take: 5, cancellationToken: cancellationToken);

        var url = Assert.Single(handler.Requests);
        var query = TestQueryString.Parse(url);

        Assert.DoesNotContain("skip", query.Keys);
        Assert.Equal("5", query["take"]);
    }
}