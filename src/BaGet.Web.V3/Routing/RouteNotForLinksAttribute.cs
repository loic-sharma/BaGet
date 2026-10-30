using System;

namespace BaGet.Web.V3.Routing;

/// <summary>
/// Lists routes of an action that answer requests but are never produced by link generation. An action with several routes
/// (<c>[Route("")]</c>, <c>[Route("index")]</c>, <c>[Route("packages")]</c>) is linked to by <c>Url.Action</c> and
/// <c>asp-action</c> at one of them, and which one is not under the
/// action's control (ASP.NET Core breaks the tie by template text). Listing the others here keeps the address that links have always
/// had: the front page lists packages at <c>/packages</c>, as the former Razor Page's <c>AddPageRoute("/Index", "/packages")</c>
/// alias did, while <c>/</c> and <c>/index</c> still reach it. The repository twins of the listed routes (<c>/r/{repo}/index</c>) are
/// not linked either (<see cref="RepositoryActionRouteConvention"/> copies the setting).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RouteNotForLinksAttribute : Attribute
{
    /// <param name="templates">The route templates of the action that links must not use (<c>""</c> is the root).</param>
    public RouteNotForLinksAttribute(params string[] templates)
    {
        Templates = templates ?? throw new ArgumentNullException(nameof(templates));
    }

    /// <summary>The route templates, as written in the <c>[Route]</c> attributes.</summary>
    public string[] Templates { get; }
}
