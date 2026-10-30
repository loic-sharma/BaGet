using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Account;
using BaGet.Identity.Application.Models.Packages;
using BaGet.Identity.Application.UseCases.Account;

namespace BaGet.Identity.Controllers.UI;

/// <summary>
/// The JSON twin of "My packages", "My access" and the "my profile" redirect (<c>/api/ui/account/packages</c>, <c>access</c>, <c>profile</c>):
/// <c>AccountPackagesController</c> calls it in process. Interactive session only: over HTTP it answers 403 to an API key and to an anonymous caller.
/// </summary>
[Route("api/ui/account")]
public sealed class AccountPackagesApiController : AccountApiControllerBase
{
    /// <inheritdoc/>
    protected override bool RequiresSignIn => true;

    /// <summary>The package IDs the user owns, per repository.</summary>
    [HttpGet("packages"), UiApi("AccountPackages", "Packages")]
    public async Task<ActionResult<ScreenResult<AccountPackagesViewModel>>> Packages(CancellationToken cancellationToken)
        => Respond(await UseCase<AccountOverviewUseCase>().ShowPackagesAsync(User, cancellationToken));

    /// <summary>What the user may do in each repository (optionally for one package ID).</summary>
    [HttpGet("access"), UiApi("AccountPackages", "Access")]
    public async Task<ActionResult<ScreenResult<AccountAccessViewModel>>> Access(string? package, CancellationToken cancellationToken)
        => Respond(await UseCase<AccountOverviewUseCase>().ShowAccessAsync(User, package, cancellationToken));

    /// <summary>The user's own profile: continues at <c>next.localUrl</c> (<c>~/profiles/{name}</c>).</summary>
    [HttpGet("profile"), UiApi("AccountPackages", "MyProfile")]
    public ActionResult<ScreenResult<EmptyScreen>> MyProfile()
        => Respond(UseCase<AccountOverviewUseCase>().ShowMyProfile(User));
}
