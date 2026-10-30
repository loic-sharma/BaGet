using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Web.V3.Controllers.UI;
using Microsoft.AspNetCore.Mvc;
using BaGet.Web.Application.Models.Browse;

namespace BaGet.Web.V3.Controllers.FrontEnd;

/// <summary>
/// The feed's statistics (<c>/stats</c>, and <c>/r/{repo}/stats</c>; 404 in a virtual repository). Replaces the <c>/Statistics</c>
/// Razor Page.
/// </summary>
[RepositoryPage(NotOnVirtual = true)]
public sealed class StatisticsController : UiControllerBase
{
    /// <summary>The statistics page.</summary>
    [Route("stats"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => Render(await Api<StatisticsApiController, StatisticsViewModel>(api => api.Get(cancellationToken)), "Index");
}
