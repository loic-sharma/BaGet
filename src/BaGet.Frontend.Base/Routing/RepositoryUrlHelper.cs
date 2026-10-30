using System;
using BaGet.Frontend.Mvc.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;

namespace BaGet.Frontend.Routing;

/// <summary>
/// Keeps links to repository pages inside the repository being browsed. ASP.NET Core carries ambient route values (here
/// <c>repo</c>) only to links that target the <i>same</i> action, so <c>Url.Action("Show", "Package")</c>, <c>asp-controller="Package"</c> and
/// <c>RedirectToAction("Index", "PackageManage")</c> on a <c>/r/second/…</c> page would otherwise drop the prefix and leave the repository.
/// This helper adds the request's <c>repo</c> as an explicit route value to every link to a controller action marked
/// <see cref="RepositoryPageAttribute"/> that does not name one itself. An explicit value always wins, and
/// <c>repo = null</c> is the way to leave for the default repository. A link to any other action is untouched, so no stray <c>?repo=</c>
/// appears on it. Razor Pages of a host are not touched either: BaGet's screens are controllers, and a screen is addressed by its controller
/// and action (<c>Url.Action</c>, <c>asp-controller</c>/<c>asp-action</c>, <c>RedirectToAction</c>), never by a page name.
/// </summary>
public class RepositoryUrlHelper : IUrlHelper
{
    private readonly IUrlHelper _inner;
    private readonly UiActionMap? _actions;

    /// <param name="inner">MVC's own helper for the same context (the endpoint-routing one), which does the work.</param>
    public RepositoryUrlHelper(IUrlHelper inner)
        : this(inner, null)
    {
    }

    internal RepositoryUrlHelper(IUrlHelper inner, UiActionMap? actions)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _actions = actions;
    }

    /// <inheritdoc/>
    public ActionContext ActionContext => _inner.ActionContext;

    /// <inheritdoc/>
    public string? Action(UrlActionContext actionContext)
    {
        if (actionContext == null) throw new ArgumentNullException(nameof(actionContext));

        if (_actions != null && ActionContext.RouteData.Values.TryGetValue(RepositoryPageAttribute.RouteKey, out var repo) && repo is string name)
        {
            var values = actionContext.Values as RouteValueDictionary
                ?? (actionContext.Values == null ? new RouteValueDictionary() : new RouteValueDictionary(actionContext.Values));

            // A missing controller or action is the current one, as MVC's own helper reads it.
            var controller = actionContext.Controller ?? Convert.ToString(ActionContext.RouteData.Values["controller"], System.Globalization.CultureInfo.InvariantCulture);
            var action = actionContext.Action ?? Convert.ToString(ActionContext.RouteData.Values["action"], System.Globalization.CultureInfo.InvariantCulture);
            if (!values.ContainsKey(RepositoryPageAttribute.RouteKey) && controller != null && action != null && _actions.IsRepositoryAction(controller, action))
            {
                values[RepositoryPageAttribute.RouteKey] = name;
                actionContext = new UrlActionContext
                {
                    Action = actionContext.Action,
                    Controller = actionContext.Controller,
                    Values = values,
                    Protocol = actionContext.Protocol,
                    Host = actionContext.Host,
                    Fragment = actionContext.Fragment,
                };
            }
        }

        return _inner.Action(actionContext);
    }

    /// <inheritdoc/>
    public string? Content(string? contentPath) => _inner.Content(contentPath);

    /// <inheritdoc/>
    public bool IsLocalUrl(string? url) => _inner.IsLocalUrl(url);

    /// <inheritdoc/>
    public string? Link(string? routeName, object? values) => _inner.Link(routeName, values);

    /// <inheritdoc/>
    public string? RouteUrl(UrlRouteContext routeContext) => _inner.RouteUrl(routeContext);
}
