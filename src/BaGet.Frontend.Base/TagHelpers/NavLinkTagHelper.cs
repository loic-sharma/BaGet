using System;
using BaGet.Frontend.Mvc.UI;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;
using BaGet.Identity.Application.Screens;

namespace BaGet.Frontend.TagHelpers;

[HtmlTargetElement(Attributes = "nav-link")]
public class NavLinkTagHelper : TagHelper
{
    private const string ActiveClass = "active";
    private const string PackagesPath = "/packages";

    private readonly IHttpContextAccessor _accessor;

    public NavLinkTagHelper(IHttpContextAccessor accessor)
    {
        _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
    }

    /// <summary>The controller the link points at (<c>asp-controller</c>); with <see cref="Action"/> it names the screen the link is active on.</summary>
    [HtmlAttributeName("asp-controller")]
    public string? Controller { get; set; }

    /// <summary>The action the link points at (<c>asp-action</c>).</summary>
    [HtmlAttributeName("asp-action")]
    public string? Action { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (!IsActiveLink(context))
        {
            return;
        }

        // This helper applies to any element carrying "nav-link", so it adds its class
        // instead of replacing the classes the markup already set.
        var existing = output.Attributes.ContainsName("class")
            ? output.Attributes["class"].Value?.ToString()
            : null;

        output.Attributes.SetAttribute("class", WithActiveClass(existing));
    }

    private static string WithActiveClass(string? classes)
    {
        if (string.IsNullOrWhiteSpace(classes))
        {
            return ActiveClass;
        }

        foreach (var name in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(name, ActiveClass, StringComparison.Ordinal))
            {
                return classes;
            }
        }

        return $"{classes} {ActiveClass}";
    }

    private bool IsActiveLink(TagHelperContext context)
    {
        // A link to a Razor Page (a host's own, in an overridden layout) never names a BaGet screen, so it is never the
        // current one here; without this it would fall into the packages-link case below.
        if (context.AllAttributes.ContainsName("asp-page")) return false;

        var httpContext = _accessor.HttpContext;
        if (httpContext == null) return false;

        // The screen of the endpoint: the controller and default GET action of the screen the current action belongs to.
        var screen = ScreenEndpoints.ScreenOf(httpContext.GetEndpoint());
        if (screen == null) return false;

        // When the link names a screen, that name alone decides whether it is active.
        // Falling through to the path check below would light up every nav link on the
        // pages that check covers.
        if (!string.IsNullOrEmpty(Controller) || !string.IsNullOrEmpty(Action))
        {
            return string.Equals(screen.Controller, Controller, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(screen.Action, Action, StringComparison.OrdinalIgnoreCase);
        }

        // The packages link names no screen: both "/" and "/packages" render the index.
        if (screen != ScreenCatalog.Index) return false;

        // Routing is case-insensitive and tolerates a trailing slash, so the comparison
        // must be too.
        #pragma warning disable CS8602
        var path = httpContext.Request.Path.Value?.TrimEnd('/');
        #pragma warning restore CS8602

        return string.IsNullOrEmpty(path)
               || string.Equals(path, PackagesPath, StringComparison.OrdinalIgnoreCase);
    }
}