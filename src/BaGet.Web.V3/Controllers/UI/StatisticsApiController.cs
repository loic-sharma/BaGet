using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using BaGet.Web.Application.Models.Browse;
using BaGet.Web.Application.UseCases;

namespace BaGet.Web.V3.Controllers.UI;

/// <summary>
/// The JSON twin of the statistics screen: <c>GET /api/ui/stats</c>, and <c>GET /api/ui/r/{repo}/stats</c> for another repository (404 in a
/// virtual one, like the page). <c>StatisticsController</c> calls it in process and renders the view.
/// </summary>
[Route("api/ui")]
[RepositoryPage(NotOnVirtual = true)]
public sealed class StatisticsApiController : ApiControllerBase
{
    private readonly StatisticsUseCase _statistics;

    /// <summary>Creates the controller.</summary>
    public StatisticsApiController(StatisticsUseCase statistics)
    {
        _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
    }

    /// <summary>The feed's statistics.</summary>
    [HttpGet("stats"), UiApi("Statistics", "Index")]
    public async Task<ActionResult<ScreenResult<StatisticsViewModel>>> Get(CancellationToken cancellationToken)
        => Respond(await _statistics.GetAsync(cancellationToken));
}
