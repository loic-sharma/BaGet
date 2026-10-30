using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Organizations;
using BaGet.Identity.Application.Models.Shared;
using BaGet.Identity.Application.UseCases.Admin;

namespace BaGet.Identity.Controllers.UI;

/// <summary>
/// The JSON twin of the administrator's organization screens (<c>/api/ui/admin/organizations</c>): the list, and one organization with its rename, member
/// and delete actions. <c>AdminOrganizationsController</c> calls it in process. Every change asks the administrator's password in the body and is
/// refused with 503 while the server is read-only.
/// </summary>
[Route("api/ui/admin/organizations")]
public sealed class AdminOrganizationsApiController : AdminApiControllerBase
{
    private AdminOrganizationsUseCase Organizations => UseCase<AdminOrganizationsUseCase>();

    /// <summary>Lists the organizations.</summary>
    [HttpGet(""), UiApi("AdminOrganizations", "Index")]
    public async Task<ActionResult<ScreenResult<AdminOrganizationsViewModel>>> List(CancellationToken cancellationToken = default)
        => Respond(await Organizations.ListAsync(cancellationToken));

    /// <summary>Shows one organization.</summary>
    [HttpGet("{name}"), UiApi("AdminOrganizations", "Detail")]
    public async Task<ActionResult<ScreenResult<OrganizationDetailViewModel>>> Get(string name, CancellationToken cancellationToken = default)
        => Respond(await Organizations.GetAsync(User, name, cancellationToken));

    /// <summary>Renames the organization.</summary>
    [HttpPost("{name}/rename"), UiApi("AdminOrganizations", "Detail", Handler = "Rename")]
    public async Task<ActionResult<ScreenResult<OrganizationDetailViewModel>>> Rename(string name, [FromBody] OrganizationRenameForm? form, CancellationToken cancellationToken = default)
        => Respond(await Organizations.RenameAsync(User, name, form?.NewName, form?.Password, cancellationToken));

    /// <summary>Deletes the organization (refused while it owns packages).</summary>
    [HttpPost("{name}/delete"), UiApi("AdminOrganizations", "Detail", Handler = "Delete")]
    public async Task<ActionResult<ScreenResult<OrganizationDetailViewModel>>> Delete(string name, [FromBody] PasswordForm? form, CancellationToken cancellationToken = default)
        => Respond(await Organizations.DeleteAsync(User, name, form?.Password, cancellationToken));

    /// <summary>Adds a user to the organization.</summary>
    [HttpPost("{name}/add-member"), UiApi("AdminOrganizations", "Detail", Handler = "AddMember")]
    public async Task<ActionResult<ScreenResult<OrganizationDetailViewModel>>> AddMember(string name, [FromBody] OrganizationMemberForm? form, CancellationToken cancellationToken = default)
        => Respond(await Organizations.AddMemberAsync(User, name, form?.UserName, form?.IsAdmin ?? false, form?.Password, cancellationToken));

    /// <summary>Makes a member an administrator of the organization, or not.</summary>
    [HttpPost("{name}/set-admin"), UiApi("AdminOrganizations", "Detail", Handler = "SetAdmin")]
    public async Task<ActionResult<ScreenResult<OrganizationDetailViewModel>>> SetAdmin(string name, [FromBody] OrganizationMemberForm? form, CancellationToken cancellationToken = default)
        => Respond(await Organizations.SetAdminAsync(User, name, form?.UserName, form?.IsAdmin ?? false, form?.Password, cancellationToken));

    /// <summary>Removes a member from the organization.</summary>
    [HttpPost("{name}/remove-member"), UiApi("AdminOrganizations", "Detail", Handler = "RemoveMember")]
    public async Task<ActionResult<ScreenResult<OrganizationDetailViewModel>>> RemoveMember(string name, [FromBody] OrganizationMemberForm? form, CancellationToken cancellationToken = default)
        => Respond(await Organizations.RemoveMemberAsync(User, name, form?.UserName, form?.Password, cancellationToken));
}
