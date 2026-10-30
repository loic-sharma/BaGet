using System;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;

namespace BaGet.Frontend.Mvc.Screens;

/// <summary>
/// <see cref="IScreenLinks"/> for the MVC screens: resolves a screen (controller and action) with the request's
/// URL helper, and builds the absolute address from <c>Site:PublicUrl</c> only (<see cref="AccountSupport.AbsoluteLink"/>).
/// </summary>
internal sealed class ScreenLinks : IScreenLinks
{
    private readonly IHttpContextAccessor _http;
    private readonly IUrlHelperFactory _factory;

    public ScreenLinks(IHttpContextAccessor http, IUrlHelperFactory factory)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <inheritdoc/>
    public string AbsoluteLink(ScreenRef screen, object values)
    {
        var context = _http.HttpContext ?? throw new InvalidOperationException("A link to a screen is built while a request is being served.");
        var url = _factory.GetUrlHelper(new ActionContext(context, context.GetRouteData(), new ActionDescriptor()));

        return AccountSupport.AbsoluteLink(context, url, screen, values);
    }
}
