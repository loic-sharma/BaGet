using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;

namespace BaGet.Frontend.Mvc.UI;

/// <summary>
/// Tells the endpoints of BaGet's own web UI from every other endpoint and names the screen an action belongs to. A screen is an action of
/// a controller implementing <see cref="IScreenController"/> (the view controllers, derived from <c>UiControllerBase</c>; the JSON twins under <c>/api/ui</c> derive from
/// <c>ApiControllerBase</c>, never from it) other than a <see cref="FallbackHandlerAttribute"/> fallback. The screen is identified by
/// the controller and the action that answers a plain <c>GET</c> for it (<see cref="ScreenRef"/>); the actions of one screen are those of the
/// controller that share its route templates (<see cref="PageHandlerAttribute"/>: handlers post to the screen's own URL).
/// </summary>
public static class ScreenEndpoints
{
    private const string ControllerSuffix = "Controller";

    private static readonly ConcurrentDictionary<MethodInfo, ScreenRef?> Screens = new();

    /// <summary>Whether <paramref name="action"/> belongs to a screen of the web UI.</summary>
    public static bool IsScreen(ActionDescriptor? action)
        => action is ControllerActionDescriptor controller
           && typeof(IScreenController).IsAssignableFrom(controller.ControllerTypeInfo.AsType())
           && controller.MethodInfo.GetCustomAttribute<FallbackHandlerAttribute>(inherit: true) == null;

    /// <summary>Whether <paramref name="endpoint"/> is an action of a screen of the web UI.</summary>
    public static bool IsScreen(Endpoint? endpoint)
        => endpoint?.Metadata.OfType<ControllerActionDescriptor>().Any(a => IsScreen(a)) == true;

    /// <summary>
    /// The screen <paramref name="action"/> belongs to (the screen itself for its <c>GET</c>, a handler of it for a named handler), or null when it is
    /// no screen action (see <see cref="IsScreen(ActionDescriptor?)"/>) or its screen has no <c>GET</c> action answering without a handler.
    /// </summary>
    public static ScreenRef? ScreenOf(ActionDescriptor? action)
        => IsScreen(action) ? ScreenOf(((ControllerActionDescriptor)action!).MethodInfo, ((ControllerActionDescriptor)action).ControllerTypeInfo.AsType()) : null;

    /// <summary>The screen of the current endpoint, or null when the endpoint is no screen action.</summary>
    public static ScreenRef? ScreenOf(Endpoint? endpoint)
        => endpoint?.Metadata.OfType<ControllerActionDescriptor>().Select(a => ScreenOf(a)).FirstOrDefault(s => s != null);

    private static ScreenRef? ScreenOf(MethodInfo method, Type controller) => Screens.GetOrAdd(method, m =>
    {
        var key = PageHandlerAttribute.ScreenOf(m);
        var page = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(c => c.GetCustomAttribute<FallbackHandlerAttribute>(inherit: true) == null
                        && (key.Length == 0 || PageHandlerAttribute.ScreenOf(c) == key)
                        && IsDefaultGet(c))
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        if (page == null) return null;

        var name = controller.Name;
        if (name.EndsWith(ControllerSuffix, StringComparison.Ordinal)) name = name.Substring(0, name.Length - ControllerSuffix.Length);

        return new ScreenRef(name, page.GetCustomAttribute<ActionNameAttribute>()?.Name ?? page.Name);
    });

    // The action that answers a plain GET: GET (or HEAD) with no named handler.
    private static bool IsDefaultGet(MethodInfo method)
    {
        var verbs = method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).SelectMany(a => a.HttpMethods).ToList();

        return verbs.Contains("GET", StringComparer.OrdinalIgnoreCase)
               && method.GetCustomAttribute<PageHandlerAttribute>(inherit: true)?.Name == null;
    }
}
