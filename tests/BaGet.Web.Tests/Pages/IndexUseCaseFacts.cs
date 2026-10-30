using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Protocol.Models;
using Moq;
using Xunit;
using BaGet.Web.Application.Models.Browse;
using BaGet.Web.Application.UseCases;

namespace BaGet.Web.Tests;

public class IndexUseCaseFacts
{
    private readonly IndexUseCase _target;
    private readonly ISearchService _searchService;

    private SearchRequest? _capturedRequest = null;
    private readonly SearchResponse _response = new();
    private readonly CancellationToken _cancellation = CancellationToken.None;

    public IndexUseCaseFacts()
    {
        var search = new Mock<ISearchService>();
        search
            .Setup(s => s.SearchAsync(It.IsAny<SearchRequest>(), _cancellation))
            .Callback((SearchRequest r, CancellationToken c) => _capturedRequest = r)
            .ReturnsAsync(_response);

        _searchService = search.Object;
        _target = new IndexUseCase(_searchService, new StatisticsService(new NullContext()));
    }

    [Fact]
    public async Task DefaultSearch()
    {
        await _target.GetAsync(new IndexQuery(), false, _cancellation);

        #pragma warning disable CS8602
        Assert.Equal(0, _capturedRequest.Skip);
        #pragma warning restore CS8602
        Assert.Equal(20, _capturedRequest.Take);
        Assert.True(_capturedRequest.IncludePrerelease);
        Assert.True(_capturedRequest.IncludeSemVer2);
        Assert.Null(_capturedRequest.PackageType);
        Assert.Null(_capturedRequest.Framework);
        Assert.Null(_capturedRequest.Query);
    }

    [Fact]
    public async Task AcceptsParameters()
    {
        var model = new IndexQuery(Query: "Hello world", PageIndex: 5, PackageType: "foo", Framework: "bar", Prerelease: false);

        await _target.GetAsync(model, false, _cancellation);

        #pragma warning disable CS8602
        Assert.Equal(80, _capturedRequest.Skip);
        #pragma warning restore CS8602
        Assert.Equal(20, _capturedRequest.Take);
        Assert.False(_capturedRequest.IncludePrerelease);
        Assert.True(_capturedRequest.IncludeSemVer2);
        Assert.Equal("foo", _capturedRequest.PackageType);
        Assert.Equal("bar", _capturedRequest.Framework);
        Assert.Equal("Hello world", _capturedRequest.Query);
    }
}
