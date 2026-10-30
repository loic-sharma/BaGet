using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Identity.Controllers.UI;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Operations;

namespace BaGet.Identity.Controllers.FrontEnd;

/// <summary>
/// Where a pushed package stands in validation (<c>/packages/{id}/validation</c>, also at <c>/r/{repo}/…</c>): the newest submission
/// of each version and its steps. It replaces the <c>/Packages/ValidationStatus</c> Razor Page (same URL, same gates); the work is done by
/// <see cref="ValidationStatusApiController"/> (the JSON twin, over <c>ValidationStatusUseCase</c>), which this controller calls in
/// process and renders.
/// </summary>
[Route("packages/{id}/validation")]
[RepositoryPage(NotOnVirtual = true)]
public sealed class ValidationStatusPageController : AccountControllerBase
{
    private const string ViewName = "Index";

    /// <inheritdoc/>
    protected override bool RequiresSignIn => true;

    /// <summary>Shows the submissions of the ID, 403 without the permission, 404 when validation never saw it.</summary>
    [HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Index([FromRoute] string? id, CancellationToken cancellationToken)
        => Render(await Api<ValidationStatusApiController, ValidationStatusViewModel>(api => api.Get(id, cancellationToken)), ViewName);
}
