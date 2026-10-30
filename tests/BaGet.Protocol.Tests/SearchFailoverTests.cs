using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Protocol.Tests;

/// <summary>
/// The service index may advertise several endpoints for the search and autocomplete
/// resources. These tests cover which failures move on to the next endpoint and which do not.
/// </summary>
public class SearchFailoverTests
{
    private const string FirstSearchUrl = "https://test.example/v3/search-one";
    private const string SecondSearchUrl = "https://test.example/v3/search-two";

    private const string FirstAutocompleteUrl = "https://test.example/v3/autocomplete-one";
    private const string SecondAutocompleteUrl = "https://test.example/v3/autocomplete-two";

    private readonly FakeHttpMessageHandler _handler = new();

    public SearchFailoverTests()
    {
        _handler.Json(
            TestData.ServiceIndexUrl,
            FakeServiceIndex.Json(
                searchUrls: new[] { FirstSearchUrl, SecondSearchUrl },
                autocompleteUrls: new[] { FirstAutocompleteUrl, SecondAutocompleteUrl }));
    }

    private NuGetClientFactory CreateFactory()
    {
        return new NuGetClientFactory(new HttpClient(_handler), TestData.ServiceIndexUrl);
    }

    [Fact]
    public async Task SearchAsync_FirstEndpointDropsTheResponse_UsesTheNextEndpoint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // A connection dropped while the response body is being read surfaces as an
        // IOException, not an HttpRequestException. The endpoint still failed.
        _handler.Throws(FirstSearchUrl, () => new IOException("The response ended prematurely."));
        _handler.Json(SecondSearchUrl, TestData.DefaultSearch);

        var response = await CreateFactory().CreateSearchClient().SearchAsync(cancellationToken: cancellationToken);

        Assert.Equal(1, response.TotalHits);
        Assert.Equal(1, _handler.RequestCount(FirstSearchUrl));
        Assert.Equal(1, _handler.RequestCount(SecondSearchUrl));
    }

    [Fact]
    public async Task AutocompleteAsync_FirstEndpointDropsTheResponse_UsesTheNextEndpoint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _handler.Throws(FirstAutocompleteUrl, () => new IOException("The response ended prematurely."));
        _handler.Json(SecondAutocompleteUrl, TestData.DefaultAutocomplete);

        var response = await CreateFactory().CreateAutocompleteClient().AutocompleteAsync(cancellationToken: cancellationToken);

        Assert.Equal(1, response.TotalHits);
        Assert.Equal(1, _handler.RequestCount(FirstAutocompleteUrl));
        Assert.Equal(1, _handler.RequestCount(SecondAutocompleteUrl));
    }

    [Fact]
    public async Task SearchAsync_FirstEndpointUnreachable_UsesTheNextEndpoint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _handler.Unreachable(FirstSearchUrl);
        _handler.Json(SecondSearchUrl, TestData.DefaultSearch);

        var response = await CreateFactory().CreateSearchClient().SearchAsync(cancellationToken: cancellationToken);

        Assert.Equal(1, response.TotalHits);
        #pragma warning disable CS8604
        Assert.Equal("Test.Package", Assert.Single(response.Data).PackageId);
        #pragma warning restore CS8604
        Assert.Equal(1, _handler.RequestCount(FirstSearchUrl));
        Assert.Equal(1, _handler.RequestCount(SecondSearchUrl));
    }

    [Fact]
    public async Task SearchAsync_FirstEndpointHasServerError_UsesTheNextEndpoint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _handler.Status(FirstSearchUrl, HttpStatusCode.ServiceUnavailable);
        _handler.Json(SecondSearchUrl, TestData.DefaultSearch);

        var response = await CreateFactory().CreateSearchClient().SearchAsync(cancellationToken: cancellationToken);

        Assert.Equal(1, response.TotalHits);
        Assert.Equal(1, _handler.RequestCount(SecondSearchUrl));
    }

    [Fact]
    public async Task SearchAsync_FirstEndpointTimesOut_UsesTheNextEndpoint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _handler.TimesOut(FirstSearchUrl);
        _handler.Json(SecondSearchUrl, TestData.DefaultSearch);

        var response = await CreateFactory().CreateSearchClient().SearchAsync(cancellationToken: cancellationToken);

        Assert.Equal(1, response.TotalHits);
        Assert.Equal(1, _handler.RequestCount(SecondSearchUrl));
    }

    [Fact]
    public async Task SearchAsync_EveryEndpointFails_ThrowsEachFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _handler.Unreachable(FirstSearchUrl);
        _handler.Unreachable(SecondSearchUrl);

        var exception = await Assert.ThrowsAsync<AggregateException>(
            () => CreateFactory().CreateSearchClient().SearchAsync(cancellationToken: cancellationToken));

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.All(exception.InnerExceptions, inner => Assert.IsType<HttpRequestException>(inner));
        Assert.Contains(FirstSearchUrl, exception.InnerExceptions[0].Message);
        Assert.Contains(SecondSearchUrl, exception.InnerExceptions[1].Message);
        Assert.Equal(1, _handler.RequestCount(FirstSearchUrl));
        Assert.Equal(1, _handler.RequestCount(SecondSearchUrl));
    }

    [Fact]
    public async Task SearchAsync_OnlyEndpointFails_ThrowsThatFailureUnwrapped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _handler.Json(
            TestData.ServiceIndexUrl,
            FakeServiceIndex.Json(searchUrls: new[] { FirstSearchUrl }));
        _handler.Unreachable(FirstSearchUrl);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => CreateFactory().CreateSearchClient().SearchAsync(cancellationToken: cancellationToken));

        Assert.Contains(FirstSearchUrl, exception.Message);
    }

    [Fact]
    public async Task SearchAsync_EndpointAnswersNotFound_DoesNotFailOver()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _handler.Status(FirstSearchUrl, HttpStatusCode.NotFound);
        _handler.Json(SecondSearchUrl, TestData.DefaultSearch);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => CreateFactory().CreateSearchClient().SearchAsync(cancellationToken: cancellationToken));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal(1, _handler.RequestCount(FirstSearchUrl));
        Assert.Equal(0, _handler.RequestCount(SecondSearchUrl));
    }

    [Fact]
    public async Task SearchAsync_EndpointAnswersBadRequest_DoesNotFailOver()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _handler.Status(FirstSearchUrl, HttpStatusCode.BadRequest);
        _handler.Json(SecondSearchUrl, TestData.DefaultSearch);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => CreateFactory().CreateSearchClient().SearchAsync(cancellationToken: cancellationToken));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal(0, _handler.RequestCount(SecondSearchUrl));
    }

    [Fact]
    public async Task SearchAsync_CallerCancels_DoesNotFailOver()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _handler.Json(FirstSearchUrl, TestData.DefaultSearch);
        _handler.Json(SecondSearchUrl, TestData.DefaultSearch);

        var factory = CreateFactory();

        // Load the service index before cancelling, so that the cancellation applies to the
        // search request and not to the service index request.
        await factory.CreateSearchClient().SearchAsync(cancellationToken: cancellationToken);

        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => factory.CreateSearchClient().SearchAsync(cancellationToken: cancellation.Token));
        }

        Assert.Equal(1, _handler.RequestCount(FirstSearchUrl));
        Assert.Equal(0, _handler.RequestCount(SecondSearchUrl));
    }

    [Fact]
    public async Task AutocompleteAsync_FirstEndpointUnreachable_UsesTheNextEndpoint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _handler.Unreachable(FirstAutocompleteUrl);
        _handler.Json(SecondAutocompleteUrl, TestData.DefaultAutocomplete);

        var response = await CreateFactory().CreateAutocompleteClient().AutocompleteAsync(cancellationToken: cancellationToken);

        Assert.Equal(1, response.TotalHits);
        #pragma warning disable CS8604
        Assert.Equal("Test.Package", Assert.Single(response.Data));
        #pragma warning restore CS8604
        Assert.Equal(1, _handler.RequestCount(FirstAutocompleteUrl));
        Assert.Equal(1, _handler.RequestCount(SecondAutocompleteUrl));
    }

    [Fact]
    public async Task AutocompleteAsync_EndpointAnswersNotFound_DoesNotFailOver()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _handler.Status(FirstAutocompleteUrl, HttpStatusCode.NotFound);
        _handler.Json(SecondAutocompleteUrl, TestData.DefaultAutocomplete);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => CreateFactory().CreateAutocompleteClient().AutocompleteAsync(cancellationToken: cancellationToken));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal(0, _handler.RequestCount(SecondAutocompleteUrl));
    }

    [Fact]
    public async Task AutocompleteAsync_EveryEndpointFails_ThrowsEachFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _handler.Unreachable(FirstAutocompleteUrl);
        _handler.Unreachable(SecondAutocompleteUrl);

        var exception = await Assert.ThrowsAsync<AggregateException>(
            () => CreateFactory().CreateAutocompleteClient().AutocompleteAsync(cancellationToken: cancellationToken));

        Assert.Equal(2, exception.InnerExceptions.Count);
    }

    [Fact]
    public async Task ListPackageVersionsAsync_FirstEndpointUnreachable_UsesTheNextEndpoint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _handler.Unreachable(FirstAutocompleteUrl);
        _handler.Json(SecondAutocompleteUrl, TestData.DefaultAutocomplete);

        var response = await CreateFactory()
            .CreateAutocompleteClient()
            .ListPackageVersionsAsync("Test.Package", cancellationToken: cancellationToken);

        Assert.Equal(1, response.TotalHits);
        Assert.Equal(1, _handler.RequestCount(FirstAutocompleteUrl));
        Assert.Equal(1, _handler.RequestCount(SecondAutocompleteUrl));
    }

    [Fact]
    public async Task ListPackageVersionsAsync_EndpointAnswersNotFound_DoesNotFailOver()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _handler.Status(FirstAutocompleteUrl, HttpStatusCode.NotFound);
        _handler.Json(SecondAutocompleteUrl, TestData.DefaultAutocomplete);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => CreateFactory().CreateAutocompleteClient().ListPackageVersionsAsync("Test.Package", cancellationToken: cancellationToken));

        Assert.Equal(0, _handler.RequestCount(SecondAutocompleteUrl));
    }

    [Fact]
    public async Task AutocompleteAsync_ServiceIndexHasNoAutocompleteResource_ReturnsNoResults()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _handler.Json(
            TestData.ServiceIndexUrl,
            FakeServiceIndex.Json(
                searchUrls: new[] { FirstSearchUrl },
                autocompleteUrls: Array.Empty<string>()));

        var response = await CreateFactory().CreateAutocompleteClient().AutocompleteAsync(cancellationToken: cancellationToken);

        Assert.Equal(0, response.TotalHits);
        #pragma warning disable CS8604
        Assert.Empty(response.Data);
        #pragma warning restore CS8604
    }

    [Fact]
    public async Task SearchAsync_DuplicateEndpoints_TriesEachEndpointOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // nuget.org advertises the same URL under several resource type versions. A failover
        // must not retry a URL that already failed.
        _handler.Json(
            TestData.ServiceIndexUrl,
            "{\"version\":\"3.0.0\",\"resources\":["
            + "{\"@id\":\"" + TestData.PackageContentUrl + "\",\"@type\":\"PackageBaseAddress/3.0.0\"},"
            + "{\"@id\":\"" + TestData.PackageMetadataUrl + "\",\"@type\":\"RegistrationsBaseUrl/3.6.0\"},"
            + "{\"@id\":\"" + FirstSearchUrl + "\",\"@type\":\"SearchQueryService/3.4.0\"},"
            + "{\"@id\":\"" + FirstSearchUrl + "\",\"@type\":\"SearchQueryService/3.0.0-beta\"},"
            + "{\"@id\":\"" + SecondSearchUrl + "\",\"@type\":\"SearchQueryService\"}]}");

        _handler.Unreachable(FirstSearchUrl);
        _handler.Json(SecondSearchUrl, TestData.DefaultSearch);

        var response = await CreateFactory().CreateSearchClient().SearchAsync(cancellationToken: cancellationToken);

        Assert.Equal(1, response.TotalHits);
        Assert.Equal(1, _handler.RequestCount(FirstSearchUrl));
        Assert.Equal(1, _handler.RequestCount(SecondSearchUrl));
    }
}