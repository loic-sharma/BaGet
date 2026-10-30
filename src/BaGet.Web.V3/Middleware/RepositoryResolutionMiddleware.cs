using System;
using System.Net;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.UI;
using BaGet.Frontend.Security;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using BaGet.Web.V3.Routing;

namespace BaGet.Web.V3.Middleware;

/// <summary>
/// Selects the repository of the request: the <c>{repo}</c> route value of an <c>/r/{repo}/</c> route, else the
/// default repository (the scoped <see cref="IRepositoryContext"/> starts there). A name that matches no repository
/// is answered 404 right here; it never falls back to the default repository, which would show or modify the wrong
/// feed. A route speaks one format's protocol (<see cref="RepositoryRouteMetadata.Format"/>; Razor Pages and unmarked
/// endpoints are NuGet's); it is answered 404 when the repository has another format, the <c>default</c> repository (always NuGet)
/// included. Must run after routing (it reads the matched endpoint's route values) and before anything queries the
/// database, so authentication follows it.
/// </summary>
public class RepositoryResolutionMiddleware
{
    /// <summary>
    /// The <see cref="HttpContext.Items"/> key the middleware sets on every request it sees; <c>RepositoryResolutionGuardFilter</c>
    /// refuses a <c>{repo}</c> route that lacks it (a host that forgot <c>UseRepositoryResolution</c>).
    /// </summary>
    public const string ResolvedItemKey = "BaGet.RepositoryResolution";

    private readonly RequestDelegate _next;

    public RepositoryResolutionMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task Invoke(
        HttpContext context,
        IRepositoryContext repository,
        IRepositoryService repositories,
        IRequestCaller caller)
    {
        context.Items[ResolvedItemKey] = true;

        // Who is asking, for code in Core that filters by the caller's rights (a virtual repository reads only the members
        // the caller may read). Credentials are read when something asks, after authentication has run.
        caller.Use(async cancellationToken =>
        {
            var resolver = context.RequestServices.GetService<RequestPrincipalResolver>();

            return resolver == null ? null : await resolver.ResolveAsync(context, cancellationToken);
        });

        // The format the matched route speaks; endpoints a host maps itself are NuGet's. A Razor Page is no protocol route:
        // the pages of every format exist under /r/{repo}/, and RepositoryPageGate decides which of them a repository has
        // (it needs the caller's rights for that, and redirects the front page of an npm repository to its own).
        var endpoint = context.GetEndpoint();
        // A page is a Razor Page of the host or an action of one of BaGet's view controllers.
        var isPage = endpoint?.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.RazorPages.PageActionDescriptor>() != null
            || ScreenEndpoints.IsScreen(endpoint)
            || endpoint?.Metadata.GetMetadata<RepositoryPageAttribute>() != null;
        var metadata = endpoint?.Metadata.GetMetadata<RepositoryRouteMetadata>();
        var format = metadata?.Format ?? RepositoryFormat.NuGet;

        // The registry routes (/v2/...) pick and authorize their repository themselves, in their own error format.
        if (metadata?.ResolvesRepositoryItself == true)
        {
            await _next(context);
            return;
        }

        if (context.GetRouteValue(Routes.RepositoryRouteKey) is string name)
        {
            // Names are lowercase (RepositoryNames); URLs are matched case-insensitively like the rest of the feed.
            var found = await repositories.FindAsync(name.ToLowerInvariant(), context.RequestAborted);

            // Only a repository of the route's format speaks this protocol; a repository of another format has its own routes.
            if (found == null || (!isPage && found.Format != format))
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                if (endpoint?.Metadata.GetMetadata<UiApiAttribute>() != null)
                {
                    // The JSON twin of a screen answers in its own shape.
                    await context.Response.WriteAsJsonAsync(
                        ScreenResult<object>.NotFound(),
                        UiApiJson.Options,
                        context.RequestAborted);
                    return;
                }

                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync("There is no such repository.", context.RequestAborted);
                return;
            }

            repository.Use(found);

            if (found.Kind == RepositoryKind.Virtual && await RedirectWriteAsync(context, repository, found))
            {
                return;
            }
        }

        else if (!isPage && format != repository.Current.Format)
        {
            // The plain routes serve the default repository, which is NuGet: another format's routes are not there.
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("There is no such repository.", context.RequestAborted);
            return;
        }

        await _next(context);
    }

    // A write to a virtual repository (a push, an unlist, a deprecation, the status of a submission) goes to its deploy
    // repository: the request works there from now on, so the access model, ownership and the indexing all apply to that
    // repository. Returns true when the request was answered here (the virtual repository has no deploy repository).
    private async Task<bool> RedirectWriteAsync(HttpContext context, IRepositoryContext repository, RepositoryInfo virtualRepository)
    {
        var route = context.GetEndpoint()?.Metadata.GetMetadata<RepositoryRouteMetadata>();
        var isWrite = route != null
            && route.WritesContent
            && (route.AuthorizesItself
                || (route.WritesOnly && !(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))));
        if (!isWrite) return false;

        var target = await context.RequestServices.GetRequiredService<IVirtualRepositories>()
            .DeployTargetAsync(virtualRepository, context.RequestAborted);
        if (target == null)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("This virtual repository has no deploy repository, so it takes no uploads.", context.RequestAborted);

            return true;
        }

        repository.Use(target);
        await _next(context);

        return true;
    }
}
