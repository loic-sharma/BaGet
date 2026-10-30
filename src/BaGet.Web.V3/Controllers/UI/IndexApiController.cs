using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using BaGet.Web.Application.Models.Browse;
using BaGet.Web.Application.Screens;
using BaGet.Web.Application.UseCases;

namespace BaGet.Web.V3.Controllers.UI;

/// <summary>
/// The JSON twin of the front page and the package list. <c>GET /api/ui/home</c> is the repository's root (<c>/</c>): the totals when the query asks for nothing,
/// else the results; <c>GET /api/ui/packages</c> is the list (<c>/packages</c>, <c>/index</c>), never the front page. Both take the page's query
/// (<c>q</c>, <c>p</c>, <c>packageType</c>, <c>framework</c>, <c>prerelease</c>, <c>sortBy</c>) and exist under <c>/api/ui/r/{repo}/</c>. In a repository of
/// another format the front page answers the format's own browse screen as <c>next</c>. A query that cannot be answered (page below 1, unknown sort) is a plain
/// 400. <c>HomeController</c> calls it in process.
/// </summary>
[Route("api/ui")]
[RepositoryPage(FrontPage = true)]
public sealed class IndexApiController : ApiControllerBase
{
    private readonly IndexUseCase _index;

    /// <summary>Creates the controller.</summary>
    public IndexApiController(IndexUseCase index)
    {
        _index = index ?? throw new ArgumentNullException(nameof(index));
    }

    /// <summary>The front page of the repository, or the results when the query asks for something.</summary>
    [HttpGet("home"), UiApi("Home", "Index")]
    public Task<ActionResult<ScreenResult<IndexScreen>>> Home(
        [FromQuery(Name = "q")] string? query,
        [FromQuery(Name = "p")] int pageIndex = 1,
        string packageType = "any",
        string framework = "any",
        bool prerelease = true,
        [FromQuery(Name = "sortBy")] SearchSort sortBy = SearchSort.Relevance,
        CancellationToken cancellationToken = default)
        => GetAsync(new IndexQuery(query, pageIndex, packageType, framework, prerelease, sortBy), !Request.Query.ContainsKey("q"), cancellationToken);

    /// <summary>One page of the package list.</summary>
    [HttpGet("packages"), UiApi("Home", "Index")]
    public Task<ActionResult<ScreenResult<IndexScreen>>> Packages(
        [FromQuery(Name = "q")] string? query,
        [FromQuery(Name = "p")] int pageIndex = 1,
        string packageType = "any",
        string framework = "any",
        bool prerelease = true,
        [FromQuery(Name = "sortBy")] SearchSort sortBy = SearchSort.Relevance,
        CancellationToken cancellationToken = default)
        => GetAsync(new IndexQuery(query, pageIndex, packageType, framework, prerelease, sortBy), false, cancellationToken);

    private async Task<ActionResult<ScreenResult<IndexScreen>>> GetAsync(IndexQuery query, bool atRepositoryRoot, CancellationToken cancellationToken)
    {
        // The binding of an in-process call is the view's: its errors are in the shared ModelState.
        if (!ModelState.IsValid || !IndexUseCase.IsValid(query)) return BadRequest();

        return Respond(await _index.GetAsync(query, atRepositoryRoot, cancellationToken));
    }
}
