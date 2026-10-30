using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Controllers.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using BaGet.Identity.Application.Models.Policies;

namespace BaGet.Identity.Controllers.FrontEnd;

/// <summary>
/// The vulnerability scanning screens: the overview (<c>/admin/scanning</c>), the dashboard of a repository
/// (<c>/admin/scanning/{name}</c>) and the policy of a project (<c>/admin/scanning/projects/{name}</c>). They replace the Razor
/// Pages of the same names (same URLs and handlers); the work is done by the JSON twin
/// (<see cref="ScanningApiController"/>) and this controller only renders its answers. Administrators only (404 for anyone else), every POST
/// is refused with 503 while the read-only gate is closed.
/// </summary>
public sealed class AdminScanningController : AccessControllerBase
{
    // ---------------------------------------------------------------- overview

    /// <summary>Shows the overview.</summary>
    [Route("admin/scanning"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => Render(await Api<ScanningApiController, ScanningViewModel>(api => api.GetOverview(cancellationToken)), "Index");

    // ---------------------------------------------------------------- one repository

    /// <summary>Shows the dashboard of a repository.</summary>
    [Route("admin/scanning/{name}"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Repository(
        string name, [FromQuery(Name = "min")] int minSeverity, [FromQuery(Name = "p")] int pageNumber, CancellationToken cancellationToken)
        => ShowRepository(await Api<ScanningApiController, ScanRepositoryViewModel>(api => api.GetRepository(name, minSeverity, pageNumber, cancellationToken)));

    /// <summary>Saves the repository policy; empty values remove it.</summary>
    [Route("admin/scanning/{name}"), HttpPost, PageHandler("Policy")]
    public async Task<IActionResult> RepositoryPolicy(
        string name, string? warn, string? blockDownload, string? blockDeploy, bool blockUnscanned, string? password, CancellationToken cancellationToken)
        => ShowRepository(await Api<ScanningApiController, ScanRepositoryViewModel>(
            api => api.SaveRepositoryPolicy(name, new ScanPolicyBody(warn, blockDownload, blockDeploy, blockUnscanned, password), cancellationToken)));

    /// <summary>Starts a scan of every artifact of the repository in the background.</summary>
    [Route("admin/scanning/{name}"), HttpPost, PageHandler("Scan")]
    public async Task<IActionResult> RepositoryScan(string name, CancellationToken cancellationToken)
        => ShowRepository(await Api<ScanningApiController, ScanRepositoryViewModel>(api => api.Scan(name, cancellationToken)));

    /// <summary>A form posted to the screen's own URL, with no handler: the screen is rendered (the page had no default handler).</summary>
    [Route("admin/scanning/{name}"), HttpPost, PageHandler, ImplicitPageHandler]
    public async Task<IActionResult> RepositoryPost(
        string name, [FromQuery(Name = "min")] int minSeverity, [FromQuery(Name = "p")] int pageNumber, CancellationToken cancellationToken)
        => ShowRepository(await Api<ScanningApiController, ScanRepositoryViewModel>(api => api.GetRepository(name, minSeverity, pageNumber, cancellationToken)));

    // ---------------------------------------------------------------- one project

    /// <summary>Shows the policy of a project.</summary>
    [Route("admin/scanning/projects/{name}"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Project(string name, CancellationToken cancellationToken)
        => ShowProject(await Api<ScanningApiController, ScanProjectViewModel>(api => api.GetProject(name, cancellationToken)));

    /// <summary>Saves the project policy; empty values remove it.</summary>
    [Route("admin/scanning/projects/{name}"), HttpPost, PageHandler("Policy")]
    public async Task<IActionResult> ProjectPolicy(
        string name, string? warn, string? blockDownload, string? blockDeploy, bool blockUnscanned, string? password, CancellationToken cancellationToken)
        => ShowProject(await Api<ScanningApiController, ScanProjectViewModel>(
            api => api.SaveProjectPolicy(name, new ScanPolicyBody(warn, blockDownload, blockDeploy, blockUnscanned, password), cancellationToken)));

    /// <summary>A form posted to the screen's own URL, with no handler: the screen is rendered (the page had no default handler).</summary>
    [Route("admin/scanning/projects/{name}"), HttpPost, PageHandler, ImplicitPageHandler]
    public async Task<IActionResult> ProjectPost(string name, CancellationToken cancellationToken)
        => ShowProject(await Api<ScanningApiController, ScanProjectViewModel>(api => api.GetProject(name, cancellationToken)));

    // ---------------------------------------------------------------- helpers

    // The use case names the package screen of each finding; the address is this controller's to build (it knows the routes).
    private IActionResult ShowRepository(UiApiResponse<ScanRepositoryViewModel> response)
        => Render(
            response,
            "Repository",
            model =>
            {
                Flash(model);
                model.LinkResolver = link => Url.Action(link.Screen.Action, link.Screen.Controller, new RouteValueDictionary(link.RouteValues));
                return model;
            });

    private IActionResult ShowProject(UiApiResponse<ScanProjectViewModel> response) => RenderFlashed(response, "Project");
}
