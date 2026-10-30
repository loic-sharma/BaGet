using System.Linq;
using System.Net;
using BaGet.Core;
using BaGet.Frontend.Mvc.UI;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using BaGet.Web.V3.Routing;

namespace BaGet.Web.V3.Filters;

/// <summary>
/// Answers a refusal by the vulnerability scanning policy (<see cref="ScanBlockedException"/>, thrown by a format's routes and
/// pages through <see cref="IScanGate"/>) with <c>403</c> and the reason, in the shape the client of the route expects: npm's
/// <c>{"error": …}</c>, the OCI registry's <c>{"errors":[{"code":"DENIED"…}]}</c>, a small page for a Razor Page, plain text for
/// the NuGet and Maven protocols (their clients print the body). Registered with MVC itself, so a host cannot forget it.
/// </summary>
public sealed class ScanBlockedFilter : IExceptionFilter
{
    /// <inheritdoc/>
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not ScanBlockedException blocked) return;

        var format = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<RepositoryRouteMetadata>()?.Format ?? RepositoryFormat.NuGet;
        var reason = blocked.Message;

        // A screen of the web UI: a Razor Page of the host, or an action of one of BaGet's view controllers.
        if (context.ActionDescriptor is PageActionDescriptor
            || ScreenEndpoints.IsScreen(context.ActionDescriptor))
        {
            var encoded = WebUtility.HtmlEncode(reason);
            context.Result = new ContentResult
            {
                StatusCode = StatusCodes.Status403Forbidden,
                ContentType = "text/html; charset=utf-8",
                Content = "<!DOCTYPE html><html><head><title>Refused</title></head><body><h1>Refused by the scanning policy</h1><p>" + encoded + "</p></body></html>",
            };
        }
        else if (format == RepositoryFormat.Npm)
        {
            context.Result = new ContentResult
            {
                StatusCode = StatusCodes.Status403Forbidden,
                ContentType = "application/json",
                Content = System.Text.Json.JsonSerializer.Serialize(new { error = reason }),
            };
        }
        else if (format == RepositoryFormat.Docker)
        {
            context.Result = new ContentResult
            {
                StatusCode = StatusCodes.Status403Forbidden,
                ContentType = "application/json",
                Content = System.Text.Json.JsonSerializer.Serialize(new { errors = new[] { new { code = "DENIED", message = reason } } }),
            };
        }
        else
        {
            context.Result = new ContentResult
            {
                StatusCode = StatusCodes.Status403Forbidden,
                ContentType = "text/plain; charset=utf-8",
                Content = reason,
            };
        }

        context.ExceptionHandled = true;
    }
}
