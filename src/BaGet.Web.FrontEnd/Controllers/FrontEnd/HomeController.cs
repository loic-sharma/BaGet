using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Web.V3.Controllers.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using BaGet.Web.Application.Screens;
using BaGet.Web.V3.Routing;
using BaGet.Web.V3.ViewModels;

namespace BaGet.Web.V3.Controllers.FrontEnd;

/// <summary>
/// The front page (<c>/</c>, with no search) and the package list (<c>/packages</c>, <c>/?q=…</c>) of a NuGet repository, at its own
/// route and at <c>/r/{repo}/…</c>. Replaces the <c>/Index</c> Razor Page: same
/// URLs and links to the front page. In a repository of another format the front page
/// redirects to the format's own browse page (<c>FrontPage</c>). Renders what <see cref="IndexApiController"/> answers.
/// </summary>
[RepositoryPage(FrontPage = true)]
public sealed class HomeController : UiControllerBase
{
    /// <summary>The front page or one page of search results.</summary>
    [Route(""), Route("index"), Route("packages"), RouteNotForLinks("", "index"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Index(IndexViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest();

        // Only the repository's root can be the front page; the address decides which question the twin is asked.
        var query = model.ToQuery();
        var response = AtRepositoryRoot()
            ? await Api<IndexApiController, IndexScreen>(api => api.Home(query.Query, query.PageIndex, query.PackageType, query.Framework, query.Prerelease, query.SortBy, cancellationToken))
            : await Api<IndexApiController, IndexScreen>(api => api.Packages(query.Query, query.PageIndex, query.PackageType, query.Framework, query.Prerelease, query.SortBy, cancellationToken));

        return Render(response, "Index", screen => model.Fill(screen));
    }

    // The front page of a repository is its root: "/" for the default, "/r/{name}" for another.
    private bool AtRepositoryRoot()
    {
        var path = Request.Path.Value?.TrimEnd('/');
        var root = HttpContext.GetRouteValue(Routes.RepositoryRouteKey) is string repository ? "/r/" + repository : string.Empty;
        return string.Equals(path ?? string.Empty, root, StringComparison.OrdinalIgnoreCase);
    }
}
