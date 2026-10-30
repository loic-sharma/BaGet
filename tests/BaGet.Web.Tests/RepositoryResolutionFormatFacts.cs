using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Moq;
using Xunit;
using BaGet.Web.V3.Middleware;
using BaGet.Web.V3.Routing;

namespace BaGet.Web.Tests;

/// <summary>
/// <see cref="RepositoryResolutionMiddleware"/> lets a request through only when the repository it names (or the default one,
/// for a plain route) has the format whose protocol the matched route speaks; a route without a format marker (the Razor
/// Pages) is NuGet's.
/// </summary>
public class RepositoryResolutionFormatFacts
{
    private static readonly RepositoryInfo NuGetRepo = new(2, "feed", RepositoryFormat.NuGet, RepositoryKind.Local);
    private static readonly RepositoryInfo NpmRepo = new(3, "js", RepositoryFormat.Npm, RepositoryKind.Local);

    private sealed record Outcome(int Status, string Body, bool Reached, RepositoryInfo Used);

    private static async Task<Outcome> RunAsync(RepositoryRouteMetadata? marker, string? repoRouteValue)
    {
        var repositories = new Mock<IRepositoryService>();
        repositories
            .Setup(r => r.FindAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? name, CancellationToken _) => name == "feed" ? NuGetRepo : name == "js" ? NpmRepo : null);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Method = "GET";

        var routeValues = new RouteValueDictionary();
        if (repoRouteValue != null) routeValues[Routes.RepositoryRouteKey] = repoRouteValue;
        context.Request.RouteValues = routeValues;

        var metadata = marker == null ? new EndpointMetadataCollection() : new EndpointMetadataCollection(marker);
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "test"));

        var repository = new RepositoryContext();
        var reached = false;
        var middleware = new RepositoryResolutionMiddleware(_ => { reached = true; return Task.CompletedTask; });
        await middleware.Invoke(context, repository, repositories.Object, new RequestCaller());

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        return new Outcome(context.Response.StatusCode, body, reached, repository.Current);
    }

    private static RepositoryRouteMetadata NpmRoute => RepositoryRouteMetadata.For(writesOnly: false, authorizesItself: false, RepositoryFormat.Npm);

    [Fact]
    public async Task ARouteWithoutAMarkerIsNuGetsAndServesNuGetRepositories()
    {
        var plain = await RunAsync(null, null);
        Assert.True(plain.Reached);
        Assert.True(plain.Used.IsDefault);

        var named = await RunAsync(RepositoryRouteMetadata.Instance, "feed");
        Assert.True(named.Reached);
        Assert.Equal(NuGetRepo, named.Used);
    }

    [Fact]
    public async Task ANuGetRouteInARepositoryOfAnotherFormatIs404()
    {
        var outcome = await RunAsync(RepositoryRouteMetadata.Instance, "js");

        Assert.False(outcome.Reached);
        Assert.Equal(404, outcome.Status);
        Assert.Equal("There is no such repository.", outcome.Body);
    }

    [Fact]
    public async Task AnNpmRouteServesAnNpmRepositoryAndNothingElse()
    {
        var ok = await RunAsync(NpmRoute, "js");
        Assert.True(ok.Reached);
        Assert.Equal(NpmRepo, ok.Used);

        var wrongFormat = await RunAsync(NpmRoute, "feed");
        Assert.False(wrongFormat.Reached);
        Assert.Equal(404, wrongFormat.Status);

        var unknown = await RunAsync(NpmRoute, "missing");
        Assert.False(unknown.Reached);
        Assert.Equal(404, unknown.Status);
    }

    [Fact]
    public async Task APlainRouteOfAnotherFormatDoesNotReachTheDefaultRepository()
    {
        // /npm/… without /r/{repo}/ would be the default repository, which is NuGet.
        var outcome = await RunAsync(NpmRoute, null);

        Assert.False(outcome.Reached);
        Assert.Equal(404, outcome.Status);
    }

    [Fact]
    public void AFormatOtherThanNuGetGetsItsOwnMarker()
    {
        Assert.Equal(RepositoryFormat.NuGet, RepositoryRouteMetadata.For(false).Format);
        Assert.Same(RepositoryRouteMetadata.Instance, RepositoryRouteMetadata.For(false));
        Assert.Equal(RepositoryFormat.Npm, NpmRoute.Format);
        Assert.True(RepositoryRouteMetadata.For(true, false, RepositoryFormat.Npm).WritesOnly);
    }
}
