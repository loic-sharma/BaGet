using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using BaGet.Web.Application.UseCases;

namespace BaGet.Web.V3.Controllers.UI;

/// <summary>
/// The JSON twin of the legal screens: <c>GET /api/ui/terms</c> and <c>GET /api/ui/privacy</c> answer the operator's Markdown
/// (<c>Site:TermsOfUsePath</c>, <c>Site:PrivacyPolicyPath</c>) rendered to safe HTML, or 404 while a file is not configured. Anonymous.
/// <c>LegalController</c> calls it in process and renders the view.
/// </summary>
[Route("api/ui")]
public sealed class LegalApiController : ApiControllerBase
{
    private readonly LegalUseCase _legal;

    /// <summary>Creates the controller.</summary>
    public LegalApiController(LegalUseCase legal)
    {
        _legal = legal ?? throw new ArgumentNullException(nameof(legal));
    }

    /// <summary>The Privacy Policy.</summary>
    [HttpGet("privacy"), UiApi("Legal", "Privacy")]
    public async Task<ActionResult<ScreenResult<LegalScreen>>> Privacy(CancellationToken cancellationToken)
        => Respond(await _legal.PrivacyAsync(cancellationToken));

    /// <summary>The Terms of Use.</summary>
    [HttpGet("terms"), UiApi("Legal", "Terms")]
    public async Task<ActionResult<ScreenResult<LegalScreen>>> Terms(CancellationToken cancellationToken)
        => Respond(await _legal.TermsAsync(cancellationToken));
}
