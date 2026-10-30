using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Controllers.UI;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Access;
using BaGet.Identity.Application.Models.Shared;

namespace BaGet.Identity.Controllers.FrontEnd;

/// <summary>
/// The groups screens of the administrator: the list with the new-group form (<c>/admin/groups</c>) and one group (<c>/admin/groups/{name}</c>:
/// rename, members, delete). They replace the Razor Pages <c>/Admin/Groups</c> and <c>/Admin/GroupDetail</c> (same URLs and handlers); the work is done by the JSON twin (<see cref="AdminGroupsApiController"/>) and this controller only renders its
/// answers. Server administrators only; membership changes and deletion ask for the password again, and every POST is refused with 503 while the
/// read-only gate is closed (<see cref="AccessControllerBase"/>).
/// </summary>
public sealed class AdminGroupsController : AccessControllerBase
{
    /// <summary>Lists the groups.</summary>
    [Route("admin/groups"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => ShowList(await Api<AdminGroupsApiController, GroupsViewModel>(api => api.List(cancellationToken)));

    /// <summary>Creates a group and opens it.</summary>
    [Route("admin/groups"), HttpPost, PageHandler]
    public async Task<IActionResult> Create(string? name, string? description, CancellationToken cancellationToken)
        => ShowList(await Api<AdminGroupsApiController, GroupsViewModel>(api => api.Create(new GroupCreation(name, description), cancellationToken)));

    /// <summary>Shows one group, with the member search for <paramref name="q"/>.</summary>
    [Route("admin/groups/{name}"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Detail(string name, string? q, CancellationToken cancellationToken)
        => ShowDetail(await Api<AdminGroupsApiController, GroupDetailViewModel>(api => api.Get(name, q, cancellationToken)));

    /// <summary>Renames the group.</summary>
    [Route("admin/groups/{name}"), HttpPost, PageHandler("Rename")]
    public async Task<IActionResult> Rename(string name, string? newName, CancellationToken cancellationToken)
        => ShowDetail(await Api<AdminGroupsApiController, GroupDetailViewModel>(api => api.Rename(name, new GroupRename(newName), cancellationToken)));

    /// <summary>Adds a user to the group.</summary>
    [Route("admin/groups/{name}"), HttpPost, PageHandler("AddMember")]
    public async Task<IActionResult> AddMember(string name, string? userId, string? password, CancellationToken cancellationToken)
        => ShowDetail(await Api<AdminGroupsApiController, GroupDetailViewModel>(api => api.AddMember(name, new GroupMemberChange(userId, password), cancellationToken)));

    /// <summary>Removes a user from the group.</summary>
    [Route("admin/groups/{name}"), HttpPost, PageHandler("RemoveMember")]
    public async Task<IActionResult> RemoveMember(string name, string? userId, string? password, CancellationToken cancellationToken)
        => ShowDetail(await Api<AdminGroupsApiController, GroupDetailViewModel>(api => api.RemoveMember(name, new GroupMemberChange(userId, password), cancellationToken)));

    /// <summary>Deletes the group with the grants and project roles that name it.</summary>
    [Route("admin/groups/{name}"), HttpPost, PageHandler("Delete")]
    public async Task<IActionResult> Delete(string name, string? password, CancellationToken cancellationToken)
        => ShowDetail(await Api<AdminGroupsApiController, GroupDetailViewModel>(api => api.Delete(name, new PasswordForm(password), cancellationToken)));

    /// <summary>A form posted to the group's own URL with no handler: the screen is rendered (the page had no default handler).</summary>
    [Route("admin/groups/{name}"), HttpPost, PageHandler, ImplicitPageHandler]
    public async Task<IActionResult> DetailPost(string name, CancellationToken cancellationToken)
        => ShowDetail(await Api<AdminGroupsApiController, GroupDetailViewModel>(api => api.Get(name, null, cancellationToken)));

    private IActionResult ShowList(UiApiResponse<GroupsViewModel> response) => RenderFlashed(response, "Index");

    private IActionResult ShowDetail(UiApiResponse<GroupDetailViewModel> response) => RenderFlashed(response, "Detail");
}
