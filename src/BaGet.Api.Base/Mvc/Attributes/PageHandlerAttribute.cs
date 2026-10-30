using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;

namespace BaGet.Frontend.Mvc.Attributes;

/// <summary>
/// Selects a controller action by the <c>handler</c> value of the request, the way a Razor Page selects <c>OnPostApprove</c>
/// for <c>?handler=Approve</c>, so every existing form, bookmark and script keeps posting to the same URL. The value is read
/// from the route values first, then from the query string (names are compared ignoring case), exactly as Razor Pages does.
/// <list type="bullet">
/// <item><description><c>[PageHandler("Approve")]</c> next to <c>[HttpPost]</c>: the action runs for a POST whose handler is
/// <c>Approve</c>.</description></item>
/// <item><description><c>[PageHandler]</c> without a name: the <em>default handler</em>, <c>OnGet</c> / <c>OnPost</c> of the
/// page. It runs when the request names no handler <b>or a handler that no action of the same screen (same
/// controller and route template, see <see cref="ScreenOf"/>; the whole controller when the action has none) and HTTP method has</b> (Razor Pages: an
/// unknown handler is the default handler). A repeated <c>handler</c> key counts by its first value, as in Razor Pages. The default handler of a screen that also has named handlers
/// must carry the attribute, else a request naming a handler would match both and be ambiguous.</description></item>
/// </list>
/// A <c>HEAD</c> request is answered by the <c>GET</c> handlers, so declare <c>[HttpGet, HttpHead]</c> on them. The attribute
/// is an action constraint, so routing does the selection; two actions with the same template, HTTP method and handler name
/// are ambiguous, as two <c>OnPostApprove</c> methods would be.
/// </summary>
/// <remarks>
/// Usage: <c>[HttpGet, PageHandler("CopyStatus")] CopyStatus()</c> answers <c>?handler=CopyStatus</c>, and <c>[HttpPost, PageHandler] Post()</c> (or just <c>[HttpPost]</c> when no sibling has a name). Forms keep
/// <c>asp-route-handler="Approve"</c> with <c>asp-controller</c>/<c>asp-action</c> (or <c>Url.Action(..., new { handler = "Approve" })</c>), which renders <c>?handler=Approve</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class PageHandlerAttribute : ActionMethodSelectorAttribute
{
    /// <summary>The route value and query string key that names the handler.</summary>
    public const string HandlerKey = "handler";

    private static readonly ConcurrentDictionary<Type, IReadOnlyList<NamedPageHandler>> Siblings = new();
    private static readonly ConcurrentDictionary<MethodInfo, string> Screens = new();

    /// <param name="name">The handler name (<c>?handler=name</c>), or null for the default handler.</param>
    public PageHandlerAttribute(string? name = null)
    {
        Name = string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>The handler name, or null for the default handler.</summary>
    public string? Name { get; }

    /// <summary>
    /// The handler a request asks for: the <c>handler</c> route value, else the <c>handler</c> query string value, else null.
    /// </summary>
    public static string? RequestedHandler(HttpContext httpContext, RouteData? routeData = null)
    {
        if (httpContext == null) throw new ArgumentNullException(nameof(httpContext));

        if (routeData?.Values.TryGetValue(HandlerKey, out var value) == true && Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) is { Length: > 0 } fromRoute)
        {
            return fromRoute;
        }

        // Razor Pages takes the first value of a repeated key (StringValues.ToString would join them with commas).
        var fromQuery = httpContext.Request.Query[HandlerKey].FirstOrDefault();

        return string.IsNullOrEmpty(fromQuery) ? null : fromQuery;
    }

    /// <inheritdoc/>
    public override bool IsValidForRequest(RouteContext routeContext, ActionDescriptor action)
    {
        if (routeContext == null) throw new ArgumentNullException(nameof(routeContext));
        if (action == null) throw new ArgumentNullException(nameof(action));

        var requested = RequestedHandler(routeContext.HttpContext, routeContext.RouteData);

        if (Name != null)
        {
            return string.Equals(Name, requested, StringComparison.OrdinalIgnoreCase);
        }

        // The default handler: valid unless a sibling of the same screen that answers this HTTP method is named like the request.
        // Only the same screen counts: a controller serving several screens must not let one screen's handler names knock out
        // another screen's default handler (Razor Pages looked at one page's handlers).
        if (requested == null || action is not ControllerActionDescriptor controllerAction)
        {
            return true;
        }

        var verb = routeContext.HttpContext.Request.Method;
        if (HttpMethods.IsHead(verb)) verb = HttpMethods.Get;

        // A descriptor built by hand may have no method: it then belongs to the whole controller.
        var screen = controllerAction.MethodInfo is { } method ? ScreenOf(method) : string.Empty;

        return !Siblings.GetOrAdd(controllerAction.ControllerTypeInfo.AsType(), NamedHandlersOf)
            .Any(h => (screen.Length == 0 || h.Screen.Length == 0 || h.Screen == screen)
                      && string.Equals(h.Name, requested, StringComparison.OrdinalIgnoreCase)
                      && (h.Verbs.Count == 0 || h.Verbs.Contains(verb, StringComparer.OrdinalIgnoreCase)));
    }

    private static IReadOnlyList<NamedPageHandler> NamedHandlersOf(Type controller)
    {
        var result = new List<NamedPageHandler>();
        foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            var handler = method.GetCustomAttribute<PageHandlerAttribute>();
            if (handler?.Name == null) continue;

            var verbs = method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).SelectMany(a => a.HttpMethods).ToList();
            result.Add(new NamedPageHandler(handler.Name, verbs, ScreenOf(method)));
        }

        return result;
    }

    /// <summary>
    /// The screen an action belongs to: the route templates of its own attributes (<c>[Route]</c>, <c>[HttpGet("x")]</c>), sorted and joined, which every
    /// action of one screen shares (a screen's handlers post to its URL); the empty key when it names none (the controller's own route is its
    /// only route, so the whole controller is one screen). An action with the empty key (a fallback such as <c>PackageManageController.NoHandler</c>)
    /// is compared with every named handler of its controller, and a named handler with the empty key counts for every screen.
    /// </summary>
    internal static string ScreenOf(MethodInfo method) => Screens.GetOrAdd(method, m => string.Join(
        "|",
        m.GetCustomAttributes<Attribute>(inherit: true)
            .OfType<IRouteTemplateProvider>()
            .Select(p => p.Template)
            .Where(template => template != null)
            .OrderBy(template => template, StringComparer.Ordinal)));
}
