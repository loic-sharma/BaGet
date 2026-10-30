using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Identity.Controllers.UI;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Organizations;
using BaGet.Identity.Application.UseCases.Account;
using BaGet.Identity.Application.UseCases.Admin;

namespace BaGet.Identity.Controllers.FrontEnd;

/// <summary>
/// The signed-in user's organizations: create one, and manage the members of those the user administers. It replaces the Razor
/// Page <c>Account/Organizations</c>: same URL, same <c>?handler=</c> forms. The work is done by <see cref="AccountOrganizationsApiController"/>.
/// </summary>
public sealed class AccountOrganizationsController : AccountControllerBase
{
    /// <inheritdoc/>
    protected override bool RequiresSignIn => true;

    /// <summary>The organizations of the user.</summary>
    [Route("account/organizations"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Organizations(CancellationToken cancellationToken)
        => Render(await Api<AccountOrganizationsApiController, OrganizationsViewModel>(api => api.Organizations(cancellationToken)), "Organizations");

    /// <summary>Create an organization.</summary>
    [Route("account/organizations"), HttpPost, PageHandler("Create")]
    public async Task<IActionResult> OrganizationsCreate(string name, CancellationToken cancellationToken)
        => Render(
            await Api<AccountOrganizationsApiController, OrganizationsViewModel>(api => api.OrganizationsCreate(new CreateOrganizationForm { Name = name }, cancellationToken)),
            "Organizations");

    /// <summary>Add a member (organization administrators only).</summary>
    [Route("account/organizations"), HttpPost, PageHandler("AddMember")]
    public async Task<IActionResult> OrganizationsAddMember(string organization, string userName, bool isAdmin, CancellationToken cancellationToken)
        => Render(
            await Api<AccountOrganizationsApiController, OrganizationsViewModel>(api => api.OrganizationsAddMember(
                new AddOrganizationMemberForm { Organization = organization, UserName = userName, IsAdmin = isAdmin },
                cancellationToken)),
            "Organizations");

    /// <summary>Remove a member (an administrator removes anyone; any member may leave).</summary>
    [Route("account/organizations"), HttpPost, PageHandler("RemoveMember")]
    public async Task<IActionResult> OrganizationsRemoveMember(string organization, string? userName, CancellationToken cancellationToken)
        => Render(
            await Api<AccountOrganizationsApiController, OrganizationsViewModel>(api => api.OrganizationsRemoveMember(
                new RemoveOrganizationMemberForm { Organization = organization, UserName = userName },
                cancellationToken)),
            "Organizations");
}
