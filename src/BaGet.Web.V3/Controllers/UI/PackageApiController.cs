using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using BaGet.Web.Application.Screens;
using BaGet.Web.Application.UseCases;

namespace BaGet.Web.V3.Controllers.UI;

/// <summary>
/// The JSON twin of the package page: <c>GET /api/ui/packages/{id}/{version?}</c>, and under <c>/api/ui/r/{repo}/</c>. The requested version, else the newest
/// listed one, with the readme as safe HTML, the install commands, dependencies, versions, owners and, for an owner or administrator, the address
/// of the management page. Anonymous when the repository is readable by anonymous visitors (the repository check of <see cref="RepositoryPageAttribute"/>).
/// <c>PackageController</c> calls it in process.
/// </summary>
[Route("api/ui")]
[RepositoryPage]
public sealed class PackageApiController : ApiControllerBase
{
    private readonly PackageUseCase _package;

    /// <summary>Creates the controller.</summary>
    public PackageApiController(PackageUseCase package)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
    }

    /// <summary>Shows the package.</summary>
    [HttpGet("packages/{id}/{version?}"), UiApi("Package", "Show")]
    public async Task<ActionResult<ScreenResult<PackageScreen>>> Get(string id, string? version, CancellationToken cancellationToken)
    {
        var result = await _package.GetAsync(id, version, User, cancellationToken);

        // The use case answers an application-relative address; the client needs one that includes the path base.
        if (result.Model?.ManageUrl is { } manage)
        {
            result = ScreenResult<PackageScreen>.Ok(result.Model with { ManageUrl = Url.Content(manage) });
        }

        return Respond(result);
    }
}
