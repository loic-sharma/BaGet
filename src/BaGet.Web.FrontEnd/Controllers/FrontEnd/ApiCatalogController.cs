using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Web.V3.Controllers.UI;
using Microsoft.AspNetCore.Mvc;
using BaGet.Web.V3.ViewModels;

namespace BaGet.Web.V3.Controllers.FrontEnd;

/// <summary>
/// The administration REST API (<c>/api/v1</c>) for people (<c>/admin/api</c>): where it is, how to authenticate (the key of an
/// administrator with the <c>admin:api</c> scope, or the global key), the password confirmation header, and every operation with the
/// page that offers the same operation. Read-only, administrators only. Replaces the <c>/Admin/Api</c> Razor Page; renders what
/// <see cref="ApiCatalogApiController"/> answers.
/// </summary>
public sealed class ApiCatalogController : AdminControllerBase
{
    /// <summary>Lists the operations.</summary>
    [Route("admin/api"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Index()
        => Render(await Api<ApiCatalogApiController, ApiCatalogViewModel>(api => api.Get()), "Index");
}
