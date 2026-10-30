using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc;
using BaGet.Web.V3.Api.Catalog;
using BaGet.Web.V3.Models.Api;
using BaGet.Web.V3.Api;
using BaGet.Web.V3.Controllers.Base;

namespace BaGet.Web.V3.Controllers.Api;

/// <summary>Permission targets (repository and package patterns with the actions principals hold): the Permissions and PermissionDetail pages' operations.</summary>
[Route(ApiCatalog.RootTemplate + "/permission-targets")]
public class PermissionTargetsController : AccessApiController
{
    /// <summary>Lists targets.</summary>
    [HttpGet]
    [ApiOperation("List permission targets", "AdminPermissions", "Index", Response = typeof(TargetSummary), PagedList = true)]
    public async Task<IActionResult> List(int? page, int? pageSize)
        => Unavailable() ?? RespondPage(await AccessAdmin.ListTargetsAsync(Cancellation), page, pageSize);

    /// <summary>One target.</summary>
    [HttpGet("{name}")]
    [ApiOperation("Show a permission target with its grants", "AdminPermissions", "Detail", Response = typeof(TargetDetails))]
    public async Task<IActionResult> Get(string name)
    {
        if (Unavailable() is { } unavailable) return unavailable;

        return await AccessAdmin.GetTargetAsync(name, Cancellation) is { } target ? Respond(target) : ApiProblems.NotFound($"permission target '{name}'");
    }

    /// <summary>Creates a target without grants.</summary>
    [HttpPost]
    [ApiOperation("Create a permission target (add grants afterwards)", "AdminPermissions", "Index", Response = typeof(TargetSummary), Status = 201)]
    public async Task<IActionResult> Create([FromBody] PermissionTargetRequest request)
    {
        if (Unavailable() is { } unavailable) return unavailable;
        ThrowIfClosed();

        int? projectKey = null;
        if (!string.IsNullOrWhiteSpace(request.Project))
        {
            var project = await AccessAdmin.GetProjectAsync(request.Project.Trim(), Cancellation)
                          ?? throw new AccessModelException($"There is no project named '{request.Project}'.");
            projectKey = project.Project.Key;
        }

        var target = await AccessAdmin.CreateTargetAsync(request.Name ?? string.Empty, request.RepositoryPatterns, request.IncludePatterns, request.ExcludePatterns, projectKey, Actor, Cancellation);

        return Respond((await AccessAdmin.GetTargetAsync(target.Name!, Cancellation))!.Target, 201, $"{ApiCatalog.Root}/permission-targets/{Uri.EscapeDataString(target.Name!)}");
    }

    /// <summary>Replaces a target's patterns.</summary>
    [HttpPut("{name}")]
    [ApiOperation("Replace a permission target's repository and package patterns", "AdminPermissions", "Detail", Confirm = true, Response = typeof(TargetSummary))]
    public async Task<IActionResult> Update(string name, [FromBody] PermissionTargetRequest request)
    {
        if (Unavailable() is { } unavailable) return unavailable;
        ThrowIfClosed();
        if (await AccessAdmin.GetTargetAsync(name, Cancellation) == null) return ApiProblems.NotFound($"permission target '{name}'");
        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        await AccessAdmin.UpdateTargetAsync(name, request.RepositoryPatterns, request.IncludePatterns, request.ExcludePatterns, Actor, Cancellation);

        return Respond((await AccessAdmin.GetTargetAsync(name, Cancellation))!.Target);
    }

    /// <summary>Deletes a target and its grants.</summary>
    [HttpDelete("{name}")]
    [ApiOperation("Delete a permission target and its grants", "AdminPermissions", "Detail", Confirm = true, Status = 204)]
    public async Task<IActionResult> Delete(string name)
    {
        if (Unavailable() is { } unavailable) return unavailable;
        ThrowIfClosed();
        if (await AccessAdmin.GetTargetAsync(name, Cancellation) == null) return ApiProblems.NotFound($"permission target '{name}'");
        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        await AccessAdmin.DeleteTargetAsync(name, Actor, Cancellation);

        return Done();
    }

    /// <summary>Sets a principal's actions through a target.</summary>
    [HttpPut("{name}/grants")]
    [ApiOperation("Give a user, group or organization actions through a permission target (none removes the grant)", "AdminPermissions", "Detail", Confirm = true, Response = typeof(TargetDetails))]
    public async Task<IActionResult> SetGrant(string name, [FromBody] GrantRequest request)
    {
        if (Unavailable() is { } unavailable) return unavailable;
        ThrowIfClosed();
        if (await AccessAdmin.GetTargetAsync(name, Cancellation) == null) return ApiProblems.NotFound($"permission target '{name}'");
        if (!TryPrincipalType(request.Type, out var type, out var problem)) return problem!;

        var actions = AccessAction.None;
        foreach (var text in request.Actions ?? new List<string>())
        {
            if (!Enum.TryParse<AccessAction>(text, ignoreCase: true, out var action) || action == AccessAction.All || action == AccessAction.None || !Enum.IsDefined(action))
            {
                return ApiProblems.BadRequest($"Unknown action '{text}'. Use Read, Annotate, Deploy, Delete or Manage.");
            }

            actions |= action;
        }

        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        await AccessAdmin.SetGrantAsync(name, type, await ResolveAsync(type, request.Principal), actions, Actor, Cancellation);

        return Respond(await AccessAdmin.GetTargetAsync(name, Cancellation));
    }

    /// <summary>Previews what patterns select.</summary>
    [HttpPost("preview")]
    [ApiOperation("Preview which repositories (and whether a package) a set of patterns selects; saves nothing", "AdminPermissions", "Index", Response = typeof(PatternPreviewDto))]
    public async Task<IActionResult> Preview([FromBody] PatternPreviewRequest request)
    {
        if (Unavailable() is { } unavailable) return unavailable;

        var all = await Service<IRepositoryService>().ListAsync(Cancellation);
        var names = all.Where(r => AccessPatterns.IsMatch(request.RepositoryPatterns, r.Name)).Select(r => r.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var package = request.TestPackage?.Trim();

        return Respond(new PatternPreviewDto(
            names,
            string.IsNullOrEmpty(package) ? null : package,
            string.IsNullOrEmpty(package) ? null : AccessPatterns.Test(request.IncludePatterns, request.ExcludePatterns, package)));
    }

    /// <summary>Re-creates the built-in groups and targets that are missing.</summary>
    [HttpPost("restore-built-ins")]
    [ApiOperation("Re-create the built-in groups and permission targets that are missing", "AdminPermissions", "Index", Confirm = true, Status = 204)]
    public async Task<IActionResult> RestoreBuiltIns()
    {
        if (Unavailable() is { } unavailable) return unavailable;
        ThrowIfClosed();
        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        await AccessAdmin.RestoreBuiltInsAsync(Actor, Cancellation);

        return Done();
    }
}
