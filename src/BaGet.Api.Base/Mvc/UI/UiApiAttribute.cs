using System;
using BaGet.Identity.Application.Screens;

namespace BaGet.Frontend.Mvc.UI;

/// <summary>
/// Marks a controller action of <c>/api/ui</c> as the JSON twin of a screen: <c>[UiApi("AdminUsers", "Index")]</c> names the screen by the
/// view controller (without the <c>Controller</c> suffix) and the default GET action that serve it (<see cref="Screen"/>), and <see cref="Handler"/> the
/// <c>?handler=</c> it mirrors (null = the screen itself). The view controllers live in the front-end assemblies the API cannot reference, so the
/// screen is a name that <c>ScreenCatalogTests</c> resolves against them. The attribute is data for the parity and coverage tests and for the OpenAPI
/// document; a twin is never a view controller, so the route snapshot of the screens (<c>ui-routes.txt</c>) does not see the API.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class UiApiAttribute : Attribute
{
    /// <param name="controller">The view controller of the screen this action serves, without the suffix (<c>"AdminUsers"</c>).</param>
    /// <param name="action">The action of that controller that shows the screen (<c>"Index"</c>).</param>
    public UiApiAttribute(string controller, string action)
    {
        if (string.IsNullOrWhiteSpace(controller)) throw new ArgumentException("A screen names its controller.", nameof(controller));
        if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("A screen names its action.", nameof(action));

        Controller = controller;
        Action = action;
    }

    /// <summary>The view controller of the screen this action serves, without the <c>Controller</c> suffix.</summary>
    public string Controller { get; }

    /// <summary>The action of the view controller that shows the screen.</summary>
    public string Action { get; }

    /// <summary>The screen this action serves, as <c>asp-controller</c>/<c>asp-action</c> name it.</summary>
    public ScreenRef Screen => new(Controller, Action);

    /// <summary>The page handler this action mirrors (<c>Approve</c> for <c>?handler=Approve</c>), or null for the screen itself (GET) or its default POST.</summary>
    public string? Handler { get; set; }
}
