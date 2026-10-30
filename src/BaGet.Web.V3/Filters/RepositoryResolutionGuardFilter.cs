using BaGet.Web.V3.Middleware;
using BaGet.Web.V3.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BaGet.Web.V3.Filters;

/// <summary>
/// Fails closed when a host forgot <c>UseRepositoryResolution</c>: a request that carries a <c>{repo}</c> route value
/// (<c>/r/{repo}/…</c>) but never went through <see cref="RepositoryResolutionMiddleware"/> would be served from the
/// default repository (a read of <c>/r/private/…</c>, a push stored in the wrong feed). It is answered <c>500</c> instead.
/// Registered with MVC itself, first, so a host cannot forget it either.
/// </summary>
public class RepositoryResolutionGuardFilter : IResourceFilter, IOrderedFilter
{
    /// <inheritdoc />
    public int Order => int.MinValue;

    /// <inheritdoc />
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        if (context.RouteData.Values.ContainsKey(Routes.RepositoryRouteKey)
            && !context.HttpContext.Items.ContainsKey(RepositoryResolutionMiddleware.ResolvedItemKey))
        {
            context.Result = new ContentResult
            {
                StatusCode = StatusCodes.Status500InternalServerError,
                ContentType = "text/plain; charset=utf-8",
                Content = "This host does not call UseRepositoryResolution(), so it cannot serve a repository other than the default one.",
            };
        }
    }

    /// <inheritdoc />
    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
