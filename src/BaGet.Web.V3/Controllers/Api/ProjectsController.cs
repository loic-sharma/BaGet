using System;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc;
using BaGet.Web.V3.Api.Catalog;
using BaGet.Web.V3.Models.Api;
using BaGet.Web.V3.Api;
using BaGet.Web.V3.Controllers.Base;

namespace BaGet.Web.V3.Controllers.Api;

/// <summary>
/// Projects (groups of repositories with their own administrators): the Projects and ProjectDetail pages' operations. Server
/// administrators only, as the API as a whole is (the pages also admit a project's own administrators; the API does not delegate).
/// </summary>
[Route(ApiCatalog.RootTemplate + "/projects")]
public class ProjectsController : AccessApiController
{
    /// <summary>Lists projects.</summary>
    [HttpGet]
    [ApiOperation("List projects with their repository and member counts", "AdminProjects", "Index", Response = typeof(ProjectSummary), PagedList = true)]
    public async Task<IActionResult> List(int? page, int? pageSize)
        => Unavailable() ?? RespondPage(await AccessAdmin.ListProjectsAsync(Cancellation), page, pageSize);

    /// <summary>One project.</summary>
    [HttpGet("{name}")]
    [ApiOperation("Show a project with its repositories, members and permission targets", "AdminProjects", "Detail", Response = typeof(ProjectDetails))]
    public async Task<IActionResult> Get(string name)
    {
        if (Unavailable() is { } unavailable) return unavailable;

        return await AccessAdmin.GetProjectAsync(name, Cancellation) is { } project ? Respond(project) : ApiProblems.NotFound($"project '{name}'");
    }

    /// <summary>Creates a project.</summary>
    [HttpPost]
    [ApiOperation("Create a project", "AdminProjects", "Index", Response = typeof(ProjectSummary), Status = 201)]
    public async Task<IActionResult> Create([FromBody] CreateProjectRequest request)
    {
        if (Unavailable() is { } unavailable) return unavailable;
        ThrowIfClosed();

        var project = await AccessAdmin.CreateProjectAsync(request.Name ?? string.Empty, string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(), Actor, Cancellation);
        var created = (await AccessAdmin.ListProjectsAsync(Cancellation)).First(p => p.Key == project.Key);

        return Respond(created, 201, $"{ApiCatalog.Root}/projects/{Uri.EscapeDataString(project.Name!)}");
    }

    /// <summary>Deletes a project; its repositories stay.</summary>
    [HttpDelete("{name}")]
    [ApiOperation("Delete a project with its members and targets (its repositories stay)", "AdminProjects", "Detail", Confirm = true, Status = 204)]
    public async Task<IActionResult> Delete(string name)
    {
        if (Unavailable() is { } unavailable) return unavailable;
        ThrowIfClosed();
        if (await AccessAdmin.GetProjectAsync(name, Cancellation) == null) return ApiProblems.NotFound($"project '{name}'");
        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        await AccessAdmin.DeleteProjectAsync(name, Actor, Cancellation);

        return Done();
    }

    /// <summary>Gives a user or group a role.</summary>
    [HttpPut("{name}/members")]
    [ApiOperation("Give a user or group a role in a project", "AdminProjects", "Detail", Confirm = true, Status = 204)]
    public async Task<IActionResult> SetMember(string name, [FromBody] ProjectMemberRequest request)
    {
        if (Unavailable() is { } unavailable) return unavailable;
        ThrowIfClosed();
        if (await AccessAdmin.GetProjectAsync(name, Cancellation) == null) return ApiProblems.NotFound($"project '{name}'");
        if (!TryPrincipalType(request.Type, out var type, out var problem)) return problem!;
        if (type == PrincipalType.Organization) return ApiProblems.BadRequest("A project member is a user or a group.");
        if (!Enum.TryParse<ProjectRole>(request.Role, ignoreCase: true, out var role) || !Enum.IsDefined(role))
        {
            return ApiProblems.BadRequest($"Unknown role '{request.Role}'. Use {string.Join(", ", Enum.GetNames(typeof(ProjectRole)))}.");
        }

        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        await AccessAdmin.SetProjectMemberAsync(name, type, await ResolveAsync(type, request.Principal), role, Actor, Cancellation);

        return Done();
    }

    /// <summary>Takes a user or group out of a project.</summary>
    [HttpDelete("{name}/members/{type}/{principal}")]
    [ApiOperation("Remove a user or group from a project", "AdminProjects", "Detail", Confirm = true, Status = 204)]
    public async Task<IActionResult> RemoveMember(string name, string type, string principal)
    {
        if (Unavailable() is { } unavailable) return unavailable;
        ThrowIfClosed();
        if (await AccessAdmin.GetProjectAsync(name, Cancellation) == null) return ApiProblems.NotFound($"project '{name}'");
        if (!TryPrincipalType(type, out var parsed, out var problem)) return problem!;
        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        await AccessAdmin.RemoveProjectMemberAsync(name, parsed, await ResolveAsync(parsed, principal), Actor, Cancellation);

        return Done();
    }

    /// <summary>Puts a repository in the project.</summary>
    [HttpPut("{name}/repositories/{repository}")]
    [ApiOperation("Put a repository in a project (it leaves any project it was in)", "AdminProjects", "Detail", Confirm = true, Status = 204)]
    public async Task<IActionResult> AssignRepository(string name, string repository)
    {
        if (Unavailable() is { } unavailable) return unavailable;
        ThrowIfClosed();
        if (await AccessAdmin.GetProjectAsync(name, Cancellation) == null) return ApiProblems.NotFound($"project '{name}'");
        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        await AccessAdmin.SetRepositoryProjectAsync(repository, name, Actor, Cancellation);

        return Done();
    }

    /// <summary>Takes a repository out of the project.</summary>
    [HttpDelete("{name}/repositories/{repository}")]
    [ApiOperation("Take a repository out of a project", "AdminProjects", "Detail", Confirm = true, Status = 204)]
    public async Task<IActionResult> UnassignRepository(string name, string repository)
    {
        if (Unavailable() is { } unavailable) return unavailable;
        ThrowIfClosed();
        var project = await AccessAdmin.GetProjectAsync(name, Cancellation);
        if (project == null) return ApiProblems.NotFound($"project '{name}'");
        if (!project.Repositories.Contains(repository, StringComparer.Ordinal)) return ApiProblems.NotFound($"repository '{repository}' in project '{name}'");
        if (await RequireConfirmationAsync() is { } refusal) return refusal;

        await AccessAdmin.SetRepositoryProjectAsync(repository, null, Actor, Cancellation);

        return Done();
    }
}
