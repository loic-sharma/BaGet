using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Controllers.UI;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Organizations;
using BaGet.Identity.Application.Models.Shared;

namespace BaGet.Identity.Controllers.FrontEnd;

/// <summary>
/// The administrator's organization screens: the list (<c>/admin/organizations</c>) and one organization
/// (<c>/admin/organizations/{name}</c>: rename, members, delete). They replace the Razor Pages <c>/Admin/Organizations</c> and
/// <c>/Admin/OrganizationDetail</c> (same URLs and handlers); the work is done by the JSON twin
/// (<see cref="AdminOrganizationsApiController"/>) and this controller only renders its answers. Every change asks the administrator's
/// password again and is refused with 503 while the read-only gate is closed.
/// </summary>
public sealed class AdminOrganizationsController : AdminControllerBase
{
    /// <summary>Lists the organizations.</summary>
    [Route("admin/organizations"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => Render(await Api<AdminOrganizationsApiController, AdminOrganizationsViewModel>(api => api.List(cancellationToken)), "Index");

    /// <summary>Shows one organization.</summary>
    [Route("admin/organizations/{name}"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Detail(string name, CancellationToken cancellationToken)
        => Show(await Api<AdminOrganizationsApiController, OrganizationDetailViewModel>(api => api.Get(name, cancellationToken)));

    /// <summary>Renames the organization.</summary>
    [Route("admin/organizations/{name}"), HttpPost, PageHandler("Rename")]
    public async Task<IActionResult> Rename(string name, string? newName, string? password, CancellationToken cancellationToken)
        => Show(await Api<AdminOrganizationsApiController, OrganizationDetailViewModel>(api => api.Rename(name, new OrganizationRenameForm(newName, password), cancellationToken)));

    /// <summary>Deletes the organization.</summary>
    [Route("admin/organizations/{name}"), HttpPost, PageHandler("Delete")]
    public async Task<IActionResult> Delete(string name, string? password, CancellationToken cancellationToken)
        => Show(await Api<AdminOrganizationsApiController, OrganizationDetailViewModel>(api => api.Delete(name, new PasswordForm(password), cancellationToken)));

    /// <summary>Adds a user to the organization.</summary>
    [Route("admin/organizations/{name}"), HttpPost, PageHandler("AddMember")]
    public async Task<IActionResult> AddMember(string name, string? userName, bool isAdmin, string? password, CancellationToken cancellationToken)
        => Show(await Api<AdminOrganizationsApiController, OrganizationDetailViewModel>(api => api.AddMember(name, new OrganizationMemberForm(userName, isAdmin, password), cancellationToken)));

    /// <summary>Makes a member an administrator of the organization, or not.</summary>
    [Route("admin/organizations/{name}"), HttpPost, PageHandler("SetAdmin")]
    public async Task<IActionResult> SetAdmin(string name, string? userName, bool isAdmin, string? password, CancellationToken cancellationToken)
        => Show(await Api<AdminOrganizationsApiController, OrganizationDetailViewModel>(api => api.SetAdmin(name, new OrganizationMemberForm(userName, isAdmin, password), cancellationToken)));

    /// <summary>Removes a member from the organization.</summary>
    [Route("admin/organizations/{name}"), HttpPost, PageHandler("RemoveMember")]
    public async Task<IActionResult> RemoveMember(string name, string? userName, string? password, CancellationToken cancellationToken)
        => Show(await Api<AdminOrganizationsApiController, OrganizationDetailViewModel>(api => api.RemoveMember(name, new OrganizationMemberForm(userName, false, password), cancellationToken)));

    /// <summary>A form posted to the screen's own URL, with no handler: the screen is rendered (the page had no default handler).</summary>
    [Route("admin/organizations/{name}"), HttpPost, PageHandler, ImplicitPageHandler]
    public async Task<IActionResult> DetailPost(string name, CancellationToken cancellationToken)
        => Show(await Api<AdminOrganizationsApiController, OrganizationDetailViewModel>(api => api.Get(name, cancellationToken)));

    private IActionResult Show(UiApiResponse<OrganizationDetailViewModel> response) => RenderFlashed(response, "Detail");
}
