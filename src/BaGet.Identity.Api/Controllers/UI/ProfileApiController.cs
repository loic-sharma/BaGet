using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Account;
using BaGet.Identity.Application.UseCases.Account;

namespace BaGet.Identity.Controllers.UI;

/// <summary>
/// The JSON twin of a user's or an organization's public page (<c>/api/ui/profiles/{name}</c>, and <c>/api/ui/r/{repo}/profiles/{name}</c>):
/// <c>ProfileController</c> calls it in process. The page is public data (names, owned package IDs with their downloads, an avatar address),
/// so unlike the other account screens an API key may read it too.
/// </summary>
[Route("api/ui/profiles")]
[RepositoryPage]
public sealed class ProfileApiController : AccountApiControllerBase
{
    /// <inheritdoc/>
    protected override bool CookieOnly => false;

    /// <summary>The profile named <paramref name="name"/>; 404 when neither a user nor an organization has it.</summary>
    [HttpGet("{name}"), UiApi("Profile", "Profile")]
    public async Task<ActionResult<ScreenResult<ProfileViewModel>>> Profile(string? name, CancellationToken cancellationToken)
        => Respond(await UseCase<ProfileUseCase>().ShowAsync(User, name, cancellationToken));
}
