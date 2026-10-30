using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Protocol.Tests;

/// <summary>
/// The factory caches the service index response. These tests cover that the cache expires,
/// so a long-lived client picks up changes to the server's resources, and that it is not
/// re-fetched before it expires.
/// </summary>
public class ServiceIndexRefreshTests
{
    private const string FirstSearchUrl = "https://test.example/v3/search-one";
    private const string SecondSearchUrl = "https://test.example/v3/search-two";

    private readonly FakeHttpMessageHandler _handler = new();
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(40);

    private DateTimeOffset _now = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private string _searchUrl = FirstSearchUrl;

    public ServiceIndexRefreshTests()
    {
        _handler.Json(
            TestData.ServiceIndexUrl,
            () => FakeServiceIndex.Json(searchUrls: new[] { _searchUrl }));

        _handler.Json(FirstSearchUrl, TestData.DefaultSearch);
        _handler.Json(SecondSearchUrl, TestData.DefaultSearch);
    }

    private NuGetClientFactory CreateFactory()
    {
        return new NuGetClientFactory(
            new HttpClient(_handler),
            TestData.ServiceIndexUrl,
            _cacheDuration,
            () => _now);
    }

    [Fact]
    public async Task ServiceIndex_BeforeTheCacheExpires_IsNotFetchedAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var factory = CreateFactory();

        await factory.CreateSearchClient().SearchAsync(cancellationToken: cancellationToken);
        _now += _cacheDuration - TimeSpan.FromSeconds(1);
        _searchUrl = SecondSearchUrl;
        await factory.CreateSearchClient().SearchAsync(cancellationToken: cancellationToken);

        Assert.Equal(1, _handler.RequestCount(TestData.ServiceIndexUrl));
        Assert.Equal(2, _handler.RequestCount(FirstSearchUrl));
        Assert.Equal(0, _handler.RequestCount(SecondSearchUrl));
    }

    [Fact]
    public async Task ServiceIndex_OnceTheCacheExpires_IsFetchedAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var factory = CreateFactory();

        await factory.CreateSearchClient().SearchAsync(cancellationToken: cancellationToken);
        _now += _cacheDuration;
        _searchUrl = SecondSearchUrl;
        await factory.CreateSearchClient().SearchAsync(cancellationToken: cancellationToken);

        // The refreshed index moved the search resource, and the client followed it.
        Assert.Equal(2, _handler.RequestCount(TestData.ServiceIndexUrl));
        Assert.Equal(1, _handler.RequestCount(FirstSearchUrl));
        Assert.Equal(1, _handler.RequestCount(SecondSearchUrl));
    }

    [Fact]
    public async Task ServiceIndex_AfterRefreshing_IsCachedAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var factory = CreateFactory();

        await factory.CreateSearchClient().SearchAsync(cancellationToken: cancellationToken);
        _now += _cacheDuration;
        await factory.CreateSearchClient().SearchAsync(cancellationToken: cancellationToken);
        await factory.CreateSearchClient().SearchAsync(cancellationToken: cancellationToken);

        Assert.Equal(2, _handler.RequestCount(TestData.ServiceIndexUrl));
    }

    [Fact]
    public async Task ServiceIndex_WhenTheRefreshFails_SurfacesTheFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var factory = CreateFactory();

        await factory.CreateSearchClient().SearchAsync(cancellationToken: cancellationToken);
        _now += _cacheDuration;
        _handler.Unreachable(TestData.ServiceIndexUrl);

        // A stale service index is not served in place of a failed refresh: an index that
        // cannot be read is reported, as its resources may have moved.
        await Assert.ThrowsAsync<HttpRequestException>(
            () => factory.CreateSearchClient().SearchAsync(cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task ServiceIndex_WithConcurrentCallers_IsFetchedOnce()
    {
        var factory = CreateFactory();

        await Task.WhenAll(Enumerable
            .Range(0, 8)
            .Select(_ => Task.Run(() => factory.CreateSearchClient().SearchAsync())));

        Assert.Equal(1, _handler.RequestCount(TestData.ServiceIndexUrl));
        Assert.Equal(8, _handler.RequestCount(FirstSearchUrl));
    }

    [Fact]
    public void ServiceIndexCacheDuration_IsBoundedByDefault()
    {
        Assert.True(NuGetClientFactory.DefaultServiceIndexCacheDuration > TimeSpan.Zero);
        Assert.True(NuGetClientFactory.DefaultServiceIndexCacheDuration <= TimeSpan.FromHours(1));
    }
}