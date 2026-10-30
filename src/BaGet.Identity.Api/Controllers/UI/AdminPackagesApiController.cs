using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Packages;
using BaGet.Identity.Application.UseCases.Packages;

namespace BaGet.Identity.Controllers.UI;

/// <summary>
/// The JSON twin of the ownerless packages screen (<c>GET /api/ui/admin/packages</c>, and <c>/api/ui/r/{repo}/admin/packages</c> like the page): the package
/// IDs that have no owner, in every repository. <c>AdminPackagesController</c> calls it in process. (The packages of one user are
/// <see cref="AdminUsersApiController.GetPackages"/>.)
/// </summary>
[Route("api/ui/admin/packages")]
public sealed class AdminPackagesApiController : AdminApiControllerBase
{
    /// <summary>Lists the ownerless IDs of every repository, <paramref name="pageNumber"/> being one-based.</summary>
    [HttpGet(""), UiApi("AdminPackages", "Index"), RepositoryPage]
    public async Task<ActionResult<ScreenResult<AdminPackagesViewModel>>> List([FromQuery(Name = "p")] int pageNumber = 1, CancellationToken cancellationToken = default)
        => Respond(await UseCase<AdminPackagesUseCase>().GetOwnerlessAsync(pageNumber, cancellationToken));
}
