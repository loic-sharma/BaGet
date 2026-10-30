using System.Reflection;
using BaGet.Frontend.Mvc.Attributes;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BaGet.Frontend.Mvc.Screens;

/// <summary>What the package-management controllers share: reading the request.</summary>
internal static class PackageControllerSupport
{
    /// <summary>
    /// The name of the handler about to run (<c>[PageHandler("Unlist")]</c> gives <c>Unlist</c>; the default handler null): for the administrator audit line.
    /// </summary>
    public static string? HandlerName(ActionExecutingContext context)
        => (context.ActionDescriptor as ControllerActionDescriptor)?.MethodInfo.GetCustomAttribute<PageHandlerAttribute>()?.Name;

    /// <summary>A route value as text (the identity of the screen's subject comes from the address, never from the body).</summary>
    public static string? Route(ActionExecutingContext context, string key)
        => context.RouteData.Values.TryGetValue(key, out var value) ? value as string : null;

    /// <summary>A form field of a POST, else a query string value: how the screens read their input.</summary>
    public static string? FormOrQuery(ActionExecutingContext context, string key)
        => FormOrQuery(context.HttpContext.Request, key);

    /// <summary>A form field of a POST, else a query string value: how the screens read their input.</summary>
    public static string? FormOrQuery(Microsoft.AspNetCore.Http.HttpRequest request, string key)
    {
        if (request.HasFormContentType && request.Form.TryGetValue(key, out var form) && form.Count > 0)
        {
            var posted = form.ToString();

            return posted.Length == 0 ? null : posted;
        }

        var query = request.Query[key].ToString();

        return query.Length == 0 ? null : query;
    }
}
