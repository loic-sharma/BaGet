using System.Collections.Generic;
using BaGet.Web.V3.Filters;
using BaGet.Web.V3.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace BaGet.Web.Tests;

/// <summary>A host that skips <c>UseRepositoryResolution</c> must not serve <c>/r/{repo}/</c> from the default repository.</summary>
public class RepositoryResolutionGuardFilterTests
{
    private static IActionResult? Run(bool hasRepoRoute, bool resolved)
    {
        var http = new DefaultHttpContext();
        if (resolved) http.Items[RepositoryResolutionMiddleware.ResolvedItemKey] = true;

        var route = new RouteData();
        if (hasRepoRoute) route.Values["repo"] = "private";

        var context = new ResourceExecutingContext(
            new ActionContext(http, route, new ActionDescriptor()),
            new List<IFilterMetadata>(),
            new List<IValueProviderFactory>());

        new RepositoryResolutionGuardFilter().OnResourceExecuting(context);

        return context.Result;
    }

    [Fact]
    public void ARepositoryRouteWithoutTheMiddlewareIsRefused()
    {
        var result = Assert.IsType<ContentResult>(Run(hasRepoRoute: true, resolved: false));

        Assert.Equal(500, result.StatusCode);
    }

    [Fact]
    public void ARepositoryRouteThatWentThroughTheMiddlewareIsServed()
        => Assert.Null(Run(hasRepoRoute: true, resolved: true));

    [Fact]
    public void ARouteWithoutARepositoryIsNotAffected()
        => Assert.Null(Run(hasRepoRoute: false, resolved: false));

    [Fact]
    public void TheFilterRunsFirst()
        => Assert.Equal(int.MinValue, new RepositoryResolutionGuardFilter().Order);
}
