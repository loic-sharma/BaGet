using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Access;
using BaGet.Identity.Application.Models.Shared;
using BaGet.Identity.Application.UseCases.Access;

namespace BaGet.Identity.Controllers.UI;

/// <summary>
/// The JSON twin of the groups screens (<c>/api/ui/admin/groups</c>): the list with the new-group form and one group (rename, members, delete).
/// <c>AdminGroupsController</c> calls it in process. Server administrators only (a session, or an <c>admin:api</c> key); every change that
/// touches membership or deletes asks the administrator's password in the body, and every write is refused with 503 while the server is read-only.
/// </summary>
[Route("api/ui/admin/groups")]
public sealed class AdminGroupsApiController : AccessApiControllerBase
{
    private AdminGroupsUseCase Groups => UseCase<AdminGroupsUseCase>();

    /// <summary>Lists the groups.</summary>
    [HttpGet(""), UiApi("AdminGroups", "Index")]
    public async Task<ActionResult<ScreenResult<GroupsViewModel>>> List(CancellationToken cancellationToken = default)
        => Respond(await Groups.ListAsync(await CallerAsync(cancellationToken), cancellationToken));

    /// <summary>Creates a group and points at it.</summary>
    [HttpPost(""), UiApi("AdminGroups", "Index")]
    public async Task<ActionResult<ScreenResult<GroupsViewModel>>> Create([FromBody] GroupCreation? form, CancellationToken cancellationToken = default)
        => Respond(await Groups.CreateAsync(await CallerAsync(cancellationToken), form ?? new GroupCreation(null, null), cancellationToken));

    /// <summary>Shows one group, with the member search for <paramref name="q"/>.</summary>
    [HttpGet("{name}"), UiApi("AdminGroups", "Detail")]
    public async Task<ActionResult<ScreenResult<GroupDetailViewModel>>> Get(string name, string? q, CancellationToken cancellationToken = default)
        => Respond(await Groups.GetAsync(await CallerAsync(cancellationToken), name, q, cancellationToken));

    /// <summary>Renames the group.</summary>
    [HttpPost("{name}/rename"), UiApi("AdminGroups", "Detail", Handler = "Rename")]
    public async Task<ActionResult<ScreenResult<GroupDetailViewModel>>> Rename(string name, [FromBody] GroupRename? form, CancellationToken cancellationToken = default)
        => Respond(await Groups.RenameAsync(await CallerAsync(cancellationToken), name, form ?? new GroupRename(null), cancellationToken));

    /// <summary>Adds a user to the group.</summary>
    [HttpPost("{name}/add-member"), UiApi("AdminGroups", "Detail", Handler = "AddMember")]
    public async Task<ActionResult<ScreenResult<GroupDetailViewModel>>> AddMember(string name, [FromBody] GroupMemberChange? form, CancellationToken cancellationToken = default)
        => Respond(await Groups.AddMemberAsync(await CallerAsync(cancellationToken), name, form ?? new GroupMemberChange(null, null), cancellationToken));

    /// <summary>Removes a user from the group.</summary>
    [HttpPost("{name}/remove-member"), UiApi("AdminGroups", "Detail", Handler = "RemoveMember")]
    public async Task<ActionResult<ScreenResult<GroupDetailViewModel>>> RemoveMember(string name, [FromBody] GroupMemberChange? form, CancellationToken cancellationToken = default)
        => Respond(await Groups.RemoveMemberAsync(await CallerAsync(cancellationToken), name, form ?? new GroupMemberChange(null, null), cancellationToken));

    /// <summary>Deletes the group with the grants and project roles that name it.</summary>
    [HttpPost("{name}/delete"), UiApi("AdminGroups", "Detail", Handler = "Delete")]
    public async Task<ActionResult<ScreenResult<GroupDetailViewModel>>> Delete(string name, [FromBody] PasswordForm? form, CancellationToken cancellationToken = default)
        => Respond(await Groups.DeleteAsync(await CallerAsync(cancellationToken), name, form ?? new PasswordForm(null), cancellationToken));
}
