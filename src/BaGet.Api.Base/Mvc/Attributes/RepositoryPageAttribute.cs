using System;
using BaGet.Core;

namespace BaGet.Frontend.Mvc.Attributes;

/// <summary>
/// Marks a controller action (or a whole controller) as a <em>repository page</em>: a page that acts on the request's
/// repository and is therefore served at its own route and at <c>/r/{repo}/…</c>, and that passes the single repository check
/// before the action runs. The marker is data on the action itself:
/// <list type="bullet">
/// <item><description><c>RepositoryActionRouteConvention</c> adds the <c>r/{repo}/</c> twin of every route of the action
/// (<c>Order</c> -1, so a link that carries a repository resolves to it and one that does not resolves to the plain route).</description></item>
/// <item><description><c>RepositoryActionFilter</c> requires Read on the repository (Deploy for an <see cref="Upload"/> page),
/// on the package named by the <c>id</c>, <c>group</c>/<c>artifact</c> or <c>image</c> route values, answers 404 to a signed-in
/// caller who may not (a visitor who is not signed in is challenged, when the host has accounts), and applies the rules below.</description></item>
/// <item><description>Links inside the page keep the repository: <c>RepositoryUrlHelper</c> adds the request's <c>repo</c> to a
/// link to such an action.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// The check runs after the controller base's own <c>OnActionExecutionAsync</c> (the controller overrides run first, like a page
/// model's did), so a model check that must precede it keeps doing so.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RepositoryPageAttribute : Attribute
{
    /// <summary>The route value that carries the repository name on the <c>/r/{repo}/</c> variant of a route.</summary>
    public const string RouteKey = "repo";

    /// <summary>
    /// The format whose repositories the page belongs to (NuGet unless set). The page answers 404 in a repository of another
    /// format; the NuGet front page (<see cref="FrontPage"/>) instead sends the visitor to the format's own browse page.
    /// Ignored when <see cref="AnyFormat"/> is true.
    /// </summary>
    public RepositoryFormat Format { get; set; } = RepositoryFormat.NuGet;

    /// <summary>The page serves a repository of every format (it names an artifact generically): <em>Scan now</em>.</summary>
    public bool AnyFormat { get; set; }

    /// <summary>
    /// A virtual repository holds nothing of its own, so the page answers 404 there (statistics, abuse reports, the owner and
    /// management tools, scanning).
    /// </summary>
    public bool NotOnVirtual { get; set; }

    /// <summary>A remote repository refuses every action of the page (publish, tags, owners), so the page answers 404 there.</summary>
    public bool NotOnRemote { get; set; }

    /// <summary>
    /// An upload page: the caller needs the right to publish (<c>IRepositoryAccess.CanWriteAsync</c>), not Read, so a
    /// Deploy-only drop box is usable. In a virtual repository the request continues in its deploy repository
    /// (<c>RepositoryPageGate.RequestedVirtualItemKey</c> remembers the virtual one for the view); without one it is 404.
    /// </summary>
    public bool Upload { get; set; }

    /// <summary>
    /// The NuGet front page of a repository (<c>/Index</c>): in a repository of another format it redirects to that format's
    /// <c>browse</c> link instead of answering 404.
    /// </summary>
    public bool FrontPage { get; set; }
}
