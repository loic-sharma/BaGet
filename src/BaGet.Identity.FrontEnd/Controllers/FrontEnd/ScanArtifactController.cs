using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Identity.Controllers.UI;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Policies;
using BaGet.Identity.Models.Packages;

namespace BaGet.Identity.Controllers.FrontEnd;

/// <summary>
/// <em>Scan now</em> for one version of an artifact of any format (<c>/scan-artifact</c>, also at <c>/r/{repo}/…</c>). It replaces
/// the <c>/Packages/Scan</c> Razor Page (same URL, same form fields, same gates: signed in, the Manage action on the artifact); the work is
/// done by <see cref="ScanArtifactApiController"/> (the JSON twin, over <c>ScanArtifactUseCase</c>), which this controller calls in
/// process and renders.
/// </summary>
[Route("scan-artifact")]
[RepositoryPage(AnyFormat = true, NotOnVirtual = true)]
public sealed class ScanArtifactController : AccountControllerBase
{
    private const string ViewName = "Index";

    /// <inheritdoc/>
    protected override bool RequiresSignIn => true;

    /// <summary>Shows the form (and the stored result) of the version.</summary>
    [HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Index(string? name, string? version, string? returnUrl, CancellationToken cancellationToken)
        => Render(await Api<ScanArtifactApiController, ScanViewModel>(api => api.Get(name, version, returnUrl, cancellationToken)), ViewName);

    /// <summary>Scans the version now (or queues it, for an image) and shows the outcome.</summary>
    [HttpPost, PageHandler]
    public async Task<IActionResult> Scan(string? name, string? version, string? returnUrl, CancellationToken cancellationToken)
        => Render(
            await Api<ScanArtifactApiController, ScanViewModel>(api => api.Scan(new ScanArtifactForm { Name = name, Version = version, ReturnUrl = returnUrl }, cancellationToken)),
            ViewName);
}
