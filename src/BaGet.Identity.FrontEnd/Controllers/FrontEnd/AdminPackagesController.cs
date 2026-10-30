using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Identity.Controllers.UI;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Packages;

namespace BaGet.Identity.Controllers.FrontEnd;

/// <summary>
/// The package IDs that have no owner, in every repository (<c>/admin/packages</c>, and <c>/r/{repo}/admin/packages</c> like the
/// page): those that predate accounts, and those left behind when their only owner's account was deleted. Only administrators and
/// the global API key may change them, so this is where an administrator finds them. It replaces the Razor Page
/// <c>/Admin/Packages</c> (same URL); the work is done by the JSON twin (<see cref="AdminPackagesApiController"/>).
/// The packages of one user are <see cref="AdminUsersController.UserPackages"/>.
/// </summary>
public sealed class AdminPackagesController : AdminControllerBase
{
    /// <summary>Lists the ownerless IDs of every repository.</summary>
    [Route("admin/packages"), HttpGet, HttpHead, PageHandler, RepositoryPage]
    public async Task<IActionResult> Index([ModelBinder(Name = "p")] int pageNumber = 1, CancellationToken cancellationToken = default)
        => Render(await Api<AdminPackagesApiController, AdminPackagesViewModel>(api => api.List(pageNumber, cancellationToken)), "Index");
}
