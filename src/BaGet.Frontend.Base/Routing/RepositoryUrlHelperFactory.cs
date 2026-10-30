using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Frontend.Routing;

/// <summary>
/// Hands out <see cref="RepositoryUrlHelper"/> wrapping MVC's default URL helper; one per request, like the default.
/// </summary>
public class RepositoryUrlHelperFactory : IUrlHelperFactory
{
    private readonly UrlHelperFactory _default = new();

    /// <inheritdoc/>
    public IUrlHelper GetUrlHelper(ActionContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        var items = context.HttpContext.Items;
        RepositoryUrlHelper? helper;
        if (items.TryGetValue(typeof(RepositoryUrlHelper), out var cached)
            && (helper = cached as RepositoryUrlHelper) != null
            && ReferenceEquals(helper.ActionContext.RouteData, context.RouteData)
            && ReferenceEquals(helper.ActionContext.ActionDescriptor, context.ActionDescriptor))
        {
            return helper;
        }

        helper = new RepositoryUrlHelper(_default.GetUrlHelper(context), context.HttpContext.RequestServices.GetService<UiActionMap>());
        items[typeof(RepositoryUrlHelper)] = helper;

        return helper;
    }
}
