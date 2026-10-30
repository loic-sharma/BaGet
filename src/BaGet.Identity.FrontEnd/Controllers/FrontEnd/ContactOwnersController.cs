using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Identity.Controllers.UI;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Packages;

namespace BaGet.Identity.Controllers.FrontEnd;

/// <summary>
/// Lets a signed-in user write to the owners of a package (<c>/packages/{id}/contact-owners</c>, also at <c>/r/{repo}/…</c>). It
/// replaces the <c>/Packages/ContactOwners</c> Razor Page (same URLs and form fields, same gates: accounts enabled, mail configured,
/// signed in); the work is done by <see cref="ContactOwnersApiController"/> (the JSON twin, over <c>ContactOwnersUseCase</c>), which this
/// controller calls in process and renders.
/// </summary>
[Route("packages/{id}/contact-owners")]
[RepositoryPage(NotOnVirtual = true)]
public sealed class ContactOwnersController : AccountControllerBase
{
    private const string ViewName = "Index";

    /// <inheritdoc/>
    protected override bool RequiresSignIn => true;

    /// <inheritdoc/>
    protected override bool RequiresEmail => true;

    /// <summary>Shows the form; 404 when the package has no owner.</summary>
    [HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Index([FromRoute] string? id, CancellationToken cancellationToken)
        => Render(await Api<ContactOwnersApiController, ContactOwnersViewModel>(api => api.Get(id, cancellationToken)), ViewName);

    /// <summary>Sends the message to the owners.</summary>
    [HttpPost, PageHandler]
    public async Task<IActionResult> Send([FromRoute] string? id, [FromForm] ContactOwnersForm form, CancellationToken cancellationToken)
        => Render(await Api<ContactOwnersApiController, ContactOwnersViewModel>(api => api.Send(id, form, cancellationToken)), ViewName);
}
