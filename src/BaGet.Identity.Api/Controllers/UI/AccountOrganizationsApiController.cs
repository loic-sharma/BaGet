using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Organizations;
using BaGet.Identity.Application.UseCases.Account;
using BaGet.Identity.Application.UseCases.Admin;

namespace BaGet.Identity.Controllers.UI;

/// <summary>
/// The JSON twin of the signed-in user's organizations (<c>/api/ui/account/organizations</c>): <c>AccountOrganizationsController</c> calls it
/// in process. Interactive session only: over HTTP it answers 403 to an API key and to an anonymous caller.
/// </summary>
[Route("api/ui/account/organizations")]
public sealed class AccountOrganizationsApiController : AccountApiControllerBase
{
    /// <inheritdoc/>
    protected override bool RequiresSignIn => true;

    /// <summary>The organizations of the user.</summary>
    [HttpGet, UiApi("AccountOrganizations", "Organizations")]
    public async Task<ActionResult<ScreenResult<OrganizationsViewModel>>> Organizations(CancellationToken cancellationToken)
        => Respond(await UseCase<AccountOverviewUseCase>().ShowOrganizationsAsync(User, cancellationToken));

    /// <summary>Create an organization.</summary>
    [HttpPost("create"), UiApi("AccountOrganizations", "Organizations", Handler = "Create")]
    public async Task<ActionResult<ScreenResult<OrganizationsViewModel>>> OrganizationsCreate([FromBody] CreateOrganizationForm? form, CancellationToken cancellationToken)
        => Respond(await UseCase<AccountOverviewUseCase>().CreateOrganizationAsync(User, form, cancellationToken));

    /// <summary>Add a member (organization administrators only; 403 otherwise).</summary>
    [HttpPost("add-member"), UiApi("AccountOrganizations", "Organizations", Handler = "AddMember")]
    public async Task<ActionResult<ScreenResult<OrganizationsViewModel>>> OrganizationsAddMember([FromBody] AddOrganizationMemberForm? form, CancellationToken cancellationToken)
        => Respond(await UseCase<AccountOverviewUseCase>().AddMemberAsync(User, form, cancellationToken));

    /// <summary>Remove a member (an administrator removes anyone; any member may leave; 403 otherwise).</summary>
    [HttpPost("remove-member"), UiApi("AccountOrganizations", "Organizations", Handler = "RemoveMember")]
    public async Task<ActionResult<ScreenResult<OrganizationsViewModel>>> OrganizationsRemoveMember([FromBody] RemoveOrganizationMemberForm? form, CancellationToken cancellationToken)
        => Respond(await UseCase<AccountOverviewUseCase>().RemoveMemberAsync(User, form, cancellationToken));
}
