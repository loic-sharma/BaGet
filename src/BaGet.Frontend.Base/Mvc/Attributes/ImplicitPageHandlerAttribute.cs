using System;

namespace BaGet.Frontend.Mvc.Attributes;

/// <summary>
/// Marks the default action (<see cref="PageHandlerAttribute"/> without a name) that stands for a verb the page had <em>no</em>
/// handler method for. A Razor Page answers every verb at its route: antiforgery runs first (a <c>POST</c> without a token is
/// <c>400</c>) and a request no handler claims renders the page. A controller route does not exist for a verb no action
/// claims (<c>404</c>), so a screen whose forms can post to its own URL without a handler (a form whose buttons carry the
/// <c>?handler=</c> as <c>formaction</c>) gets such an action, which renders the screen like the page did. It is not a handler of
/// the page: the route contract (<c>ui-routes.txt</c>) does not list it.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ImplicitPageHandlerAttribute : Attribute
{
}
