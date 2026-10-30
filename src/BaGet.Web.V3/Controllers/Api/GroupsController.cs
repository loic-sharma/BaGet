using System;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc;
using BaGet.Web.V3.Api.Catalog;
using BaGet.Web.V3.Models.Api;
using BaGet.Web.V3.Api;
using BaGet.Web.V3.Controllers.Base;

namespace BaGet.Web.V3.Controllers.Api;

/// <summary>Groups of users: the Groups and GroupDetail pages' operations.</summary>
[Route(ApiCatalog.RootTemplate + "/groups")]
public class GroupsController : AccessApiController
{
    /// <summary>Lists groups.</summary>
    [HttpGet]
    [ApiOperation("List groups with their member counts", "AdminGroups", "Index", Response = typeof(GroupSummary), PagedList = true)]
    public async Task<IActionResult> List(int? page, int? pageSize)
        => Unavailable() ?? RespondPage(await AccessAdmin.ListGroupsAsync(Cancellation), page, pageSize);

    /// <summary>One group.</summary>
    [HttpGet("{name}")]
    [ApiOperation("Show a group with its members and the grants naming it", "AdminGroups", "Detail", Response = typeof(GroupDetails))]
    public async Task<IActionResult> Get(string name)
    {
        if (Unavailable() is { } unavailable) return unavailable;

        return await AccessAdmin.GetGroupAsync(name, Cancellation) is { } group ? Respond(group) : ApiProblems.NotFound($"group '{name}'");
    }

    /// <summary>Creates a group.</summary>
    [HttpPost]
    [ApiOperation("Create a group", "AdminGroups", "Index", Response = typeof(GroupSummary), Status = 201)]
    public async Task<IActionResult> Create([FromBody] CreateGroupRequest request)
    {
        if (Unavailable() is { } unavailable) return unavailable;
        ThrowIfClosed();

        var group = await AccessAdmin.CreateGroupAsync(request.Name ?? string.Empty, request.Description, Actor, Cancellation);

        return Respond((await AccessAdmin.GetGroupAsync(group.Name!, Cancellation))!.Group, 201, $"{ApiCatalog.Root}/groups/{Uri.EscapeDataString(group.Name!)}");
    }

    /// <summary>Renames a group.</summary>
    [HttpPatch("{name}")]
    [ApiOperation("Rename a group", "AdminGroups", "Detail", Response = typeof(GroupSummary))]
    public async Task<IActionResult> Rename(string name, [FromBody] RenameGroupRequest request)
    {
        if (Unavailable() is { } unavailable) return unavailable;
        ThrowIfClosed();
        if (await AccessAdmin.GetGroupAsync(name, Cancellation) == null) return ApiProblems.NotFound($"group '{name}'");

        await AccessAdmin.RenameGroupAsync(name, request.NewName ?? string.Empty, Actor, Cancellation);

        return Respond((await AccessAdmin.GetGroupAsync(request.NewName!.Trim(), Cancellation))?.Group);
    }

    /// <summary>Deletes a group.</summary>
    [HttpDelete("{name}")]
    [ApiOperation("Delete a group with the grants and project roles that name it", "AdminGroups", "Detail", Confirm = true, Status = 204)]
    public async Task<IActionResult> Delete(string name)
    {
        if (Unavailable() is { } unavailable) return unavailable;
        ThrowIfClosed();
        if (await AccessAdmin.GetGroupAsync(name, Cancellation) == null) return ApiProblems.NotFound($"group '{name}'");
        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        await AccessAdmin.DeleteGroupAsync(name, Actor, Cancellation);

        return Done();
    }

    /// <summary>Adds a user to a group.</summary>
    [HttpPut("{name}/members/{user}")]
    [ApiOperation("Add a user to a group", "AdminGroups", "Detail", Confirm = true, Status = 204)]
    public async Task<IActionResult> AddMember(string name, string user)
    {
        if (Unavailable() is { } unavailable) return unavailable;
        ThrowIfClosed();
        if (await AccessAdmin.GetGroupAsync(name, Cancellation) == null) return ApiProblems.NotFound($"group '{name}'");
        if (await AccessAdmin.ResolvePrincipalAsync(PrincipalType.User, user, Cancellation) is not { } userId) return ApiProblems.NotFound($"user '{user}'");
        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        await AccessAdmin.AddGroupMemberAsync(name, userId, Actor, Cancellation);

        return Done();
    }

    /// <summary>Removes a user from a group.</summary>
    [HttpDelete("{name}/members/{user}")]
    [ApiOperation("Remove a user from a group", "AdminGroups", "Detail", Confirm = true, Status = 204)]
    public async Task<IActionResult> RemoveMember(string name, string user)
    {
        if (Unavailable() is { } unavailable) return unavailable;
        ThrowIfClosed();
        if (await AccessAdmin.GetGroupAsync(name, Cancellation) == null) return ApiProblems.NotFound($"group '{name}'");
        if (await AccessAdmin.ResolvePrincipalAsync(PrincipalType.User, user, Cancellation) is not { } userId) return ApiProblems.NotFound($"user '{user}'");
        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        await AccessAdmin.RemoveGroupMemberAsync(name, userId, Actor, Cancellation);

        return Done();
    }
}
