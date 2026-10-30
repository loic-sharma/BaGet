using System;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using BaGet.Web.V3.Api.Catalog;
using BaGet.Web.V3.ViewModels;
using BaGet.Web.V3.Controllers.Base;

namespace BaGet.Web.V3.Controllers.UI;

/// <summary>
/// The JSON twin of the REST API page: <c>GET /api/ui/admin/api</c> answers where the API is, the confirmation header and every operation with
/// the page that offers it. The operations are read from the controllers of the API (<see cref="ApiCatalog"/>), the same source as the OpenAPI
/// documents, so the list cannot be stale. Administrators only (a key needs the <c>admin:api</c> scope). The list is built here, not in a use
/// case, because it is read from MVC's action descriptors. <c>ApiCatalogController</c> calls it in process.
/// </summary>
[Route("api/ui/admin")]
public sealed class ApiCatalogApiController : AdminApiControllerBase
{
    private readonly IActionDescriptorCollectionProvider _actions;

    /// <summary>Creates the controller.</summary>
    public ApiCatalogApiController(IActionDescriptorCollectionProvider actions)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    /// <summary>The operations of the REST API.</summary>
    [HttpGet("api"), UiApi("ApiCatalog", "Index")]
    public Task<ActionResult<ScreenResult<ApiCatalogViewModel>>> Get()
    {
        var endpoints = ApiCatalog.Endpoints(_actions)
            .Select(e => new ApiEndpointSummary(e.Method, e.Path, e.Operation.Summary, e.Operation.UiScreen, e.Operation.Confirm))
            .ToList();
        var model = new ApiCatalogViewModel(
            endpoints,
            $"{Request.Scheme}://{Request.Host}{Request.PathBase}{ApiCatalog.Root}",
            AdminApiController.ConfirmHeader);

        return Task.FromResult(Respond(ScreenResult<ApiCatalogViewModel>.Ok(model)));
    }
}
