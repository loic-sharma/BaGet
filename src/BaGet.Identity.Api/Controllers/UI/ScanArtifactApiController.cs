using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using BaGet.Identity.Application.Models.Policies;
using BaGet.Identity.Application.UseCases.Packages;
using BaGet.Identity.Models.Packages;

namespace BaGet.Identity.Controllers.UI;

/// <summary>
/// The JSON twin of <em>Scan now</em>: <c>GET /api/ui/scan-artifact?name=&amp;version=</c> (the stored result) and <c>POST
/// /api/ui/scan-artifact</c> with <c>{ "name": …, "version": … }</c> (scans now, or queues an image; also under <c>/api/ui/r/{repo}/…</c>).
/// 403 without the Manage action on the artifact. A refused scan answers with the screen under 409 (scanning unavailable) or 404 (no such
/// version). Interactive session only: over HTTP it answers 403 to an API key and to an anonymous caller. <c>ScanArtifactController</c> calls it in process and renders the view.
/// </summary>
[Route("api/ui/scan-artifact")]
[RepositoryPage(AnyFormat = true, NotOnVirtual = true)]
public sealed class ScanArtifactApiController : AccountApiControllerBase
{
    /// <inheritdoc/>
    protected override bool RequiresSignIn => true;

    private ScanArtifactUseCase UseCase => HttpContext.RequestServices.GetRequiredService<ScanArtifactUseCase>();

    /// <summary>Shows the form (and the stored result) of the version.</summary>
    [HttpGet, UiApi("ScanArtifact", "Index")]
    public async Task<ActionResult<ScreenResult<ScanViewModel>>> Get(string? name, string? version, string? returnUrl, CancellationToken cancellationToken)
        => Respond(await UseCase.GetAsync(User, name, version, returnUrl, BackUrl(returnUrl), cancellationToken));

    /// <summary>Scans the version now (or queues it, for an image) and shows the outcome.</summary>
    [HttpPost, UiApi("ScanArtifact", "Index")]
    public async Task<ActionResult<ScreenResult<ScanViewModel>>> Scan([FromBody] ScanArtifactForm? form, CancellationToken cancellationToken)
        => Respond(await UseCase.ScanAsync(User, form?.Name, form?.Version, form?.ReturnUrl, BackUrl(form?.ReturnUrl), cancellationToken));

    // The return address when it is local, else the home page (under the path base).
    private string BackUrl(string? returnUrl)
        => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Content("~/") ?? "/";
}
