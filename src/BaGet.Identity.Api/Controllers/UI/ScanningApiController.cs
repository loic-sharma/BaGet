using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Policies;
using BaGet.Identity.Application.UseCases.Policies;

namespace BaGet.Identity.Controllers.UI;

/// <summary>
/// The JSON twin of the vulnerability scanning screens (<c>/api/ui/admin/scanning</c>): the overview, the dashboard of a repository (findings,
/// policy, <em>Scan now</em>) and the policy of a project. <c>AdminScanningController</c> calls it in process. Administrators only; saving a
/// policy asks the administrator's password in the body; every write is refused with 503 while the server is read-only. The links to the package
/// screens of the findings are <c>packageLinks</c> (controller, action and route values), not addresses.
/// </summary>
[Route("api/ui/admin/scanning")]
public sealed class ScanningApiController : AccessApiControllerBase
{
    private ScanningUseCase Scanning => UseCase<ScanningUseCase>();

    private string ReturnPage => "~" + Request.Path + Request.QueryString;

    // ---------------------------------------------------------------- overview

    /// <summary>The overview of every repository and project.</summary>
    [HttpGet(""), UiApi("AdminScanning", "Index")]
    public async Task<ActionResult<ScreenResult<ScanningViewModel>>> GetOverview(CancellationToken cancellationToken = default)
        => Respond(await Scanning.GetOverviewAsync(cancellationToken));

    // ---------------------------------------------------------------- one repository

    /// <summary>The dashboard of a repository: <c>?min=</c> is the lowest severity listed (0 to 3), <c>?p=</c> the page.</summary>
    [HttpGet("{name}"), UiApi("AdminScanning", "Repository")]
    public async Task<ActionResult<ScreenResult<ScanRepositoryViewModel>>> GetRepository(
        string name, [FromQuery(Name = "min")] int minSeverity, [FromQuery(Name = "p")] int pageNumber, CancellationToken cancellationToken = default)
        => Respond(await Scanning.GetRepositoryAsync(User, name, minSeverity, pageNumber, ReturnPage, cancellationToken));

    /// <summary>Saves the repository policy; empty values remove it.</summary>
    [HttpPost("{name}/policy"), UiApi("AdminScanning", "Repository", Handler = "Policy")]
    public async Task<ActionResult<ScreenResult<ScanRepositoryViewModel>>> SaveRepositoryPolicy(
        string name, [FromBody] ScanPolicyBody? body, CancellationToken cancellationToken = default)
        => Respond(await Scanning.SaveRepositoryPolicyAsync(User, name, body, cancellationToken));

    /// <summary>Starts a scan of every artifact of the repository in the background.</summary>
    [HttpPost("{name}/scan"), UiApi("AdminScanning", "Repository", Handler = "Scan")]
    public async Task<ActionResult<ScreenResult<ScanRepositoryViewModel>>> Scan(string name, CancellationToken cancellationToken = default)
        => Respond(await Scanning.ScanAsync(User, name, cancellationToken));

    // ---------------------------------------------------------------- one project

    /// <summary>The scanning policy of a project.</summary>
    [HttpGet("projects/{name}"), UiApi("AdminScanning", "Project")]
    public async Task<ActionResult<ScreenResult<ScanProjectViewModel>>> GetProject(string name, CancellationToken cancellationToken = default)
        => Respond(await Scanning.GetProjectAsync(User, name, ReturnPage, cancellationToken));

    /// <summary>Saves the project policy; empty values remove it.</summary>
    [HttpPost("projects/{name}/policy"), UiApi("AdminScanning", "Project", Handler = "Policy")]
    public async Task<ActionResult<ScreenResult<ScanProjectViewModel>>> SaveProjectPolicy(
        string name, [FromBody] ScanPolicyBody? body, CancellationToken cancellationToken = default)
        => Respond(await Scanning.SaveProjectPolicyAsync(User, name, body, cancellationToken));
}
