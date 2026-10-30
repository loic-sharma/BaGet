using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BaGet.Protocol.Internal;
using Xunit;

namespace BaGet.Protocol.Tests;

public class RawAutocompleteClientTests : IClassFixture<ProtocolFixture>
{
    private readonly RawAutocompleteClient _target;

    public RawAutocompleteClientTests(ProtocolFixture fixture)
    {
        _target = fixture.AutocompleteClient;
    }

    [Fact]
    public async Task GetDefaultAutocompleteResults()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var response = await _target.AutocompleteAsync(cancellationToken: cancellationToken);

        Assert.NotNull(response);
        Assert.Equal(1, response.TotalHits);
        #pragma warning disable CS8604
        Assert.Equal("Test.Package", Assert.Single(response.Data));
        #pragma warning restore CS8604
    }

    [Fact]
    public async Task AddsDefaultAutocompleteParameters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new RecordingHttpMessageHandler(TestData.DefaultAutocomplete);
        var target = new RawAutocompleteClient(new HttpClient(handler), TestData.AutocompleteUrl);

        await target.AutocompleteAsync(cancellationToken: cancellationToken);

        var url = Assert.Single(handler.Requests);
        var query = TestQueryString.Parse(url);

        Assert.Equal("test.example", url.Host);
        Assert.Equal("/v3/autocomplete", url.AbsolutePath);
        Assert.Equal(
            new[] { "prerelease", "semVerLevel", "take" },
            query.Keys.OrderBy(k => k).ToArray());
        Assert.Equal("20", query["take"]);
        Assert.Equal("True", query["prerelease"]);
        Assert.Equal("2.0.0", query["semVerLevel"]);
    }

    [Fact]
    public async Task AddsAutocompleteParameters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new RecordingHttpMessageHandler(TestData.DefaultAutocomplete);
        var target = new RawAutocompleteClient(new HttpClient(handler), TestData.AutocompleteUrl);

        await target.AutocompleteAsync(
            "query",
            skip: 2,
            take: 5,
            includePrerelease: false,
            includeSemVer2: false, cancellationToken);

        var url = Assert.Single(handler.Requests);
        var query = TestQueryString.Parse(url);

        Assert.Equal("/v3/autocomplete", url.AbsolutePath);
        Assert.Equal(
            new[] { "q", "skip", "take" },
            query.Keys.OrderBy(k => k).ToArray());
        Assert.Equal("2", query["skip"]);
        Assert.Equal("5", query["take"]);
        Assert.Equal("query", query["q"]);
    }

    [Fact]
    public async Task ListsPackageVersions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var response = await _target.ListPackageVersionsAsync("Test.Package", cancellationToken: cancellationToken);

        Assert.NotNull(response);
        Assert.Equal(3, response.TotalHits);
        #pragma warning disable CS8604
        Assert.Equal(new[] { "1.0.0", "2.0.0", "3.0.0" }, response.Data.ToArray());
        #pragma warning restore CS8604
    }

    [Fact]
    public async Task AddsDefaultListPackageVersionsParameters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new RecordingHttpMessageHandler(TestData.AutocompleteVersions);
        var target = new RawAutocompleteClient(new HttpClient(handler), TestData.AutocompleteUrl);

        await target.ListPackageVersionsAsync("Test.Package", cancellationToken: cancellationToken);

        var url = Assert.Single(handler.Requests);
        var query = TestQueryString.Parse(url);

        Assert.Equal("/v3/autocomplete", url.AbsolutePath);
        Assert.Equal(
            new[] { "id", "prerelease", "semVerLevel" },
            query.Keys.OrderBy(k => k).ToArray());
        Assert.Equal("Test.Package", query["id"]);
        Assert.Equal("True", query["prerelease"]);
        Assert.Equal("2.0.0", query["semVerLevel"]);
    }

    [Fact]
    public async Task AddsListPackageVersionsParameters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new RecordingHttpMessageHandler(TestData.AutocompleteVersions);
        var target = new RawAutocompleteClient(new HttpClient(handler), TestData.AutocompleteUrl);

        await target.ListPackageVersionsAsync(
            "query",
            includePrerelease: false,
            includeSemVer2: false, cancellationToken);

        var url = Assert.Single(handler.Requests);
        var query = TestQueryString.Parse(url);

        Assert.Equal("/v3/autocomplete", url.AbsolutePath);
        Assert.Equal(new[] { "id" }, query.Keys.ToArray());
        Assert.Equal("query", query["id"]);
    }
}