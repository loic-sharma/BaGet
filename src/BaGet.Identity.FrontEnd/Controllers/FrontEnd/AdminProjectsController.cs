using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Controllers.UI;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Access;
using BaGet.Identity.Application.Models.Shared;
using BaGet.Identity.Application.UseCases.Access;

namespace BaGet.Identity.Controllers.FrontEnd;

/// <summary>
/// The project screens: the list with the new-project form (<c>/admin/projects</c>) and one project (<c>/admin/projects/{name}</c>:
/// repositories, members, the project's permission targets, delete). They replace the Razor Pages <c>/Admin/Projects</c> and
/// <c>/Admin/ProjectDetail</c> (same URLs and handlers); the work is done by the JSON twin
/// (<see cref="AdminProjectsApiController"/>) and this controller only renders its answers. Server administrators, and the administrators of a
/// project for the projects they administer (the project is the <c>name</c> route value; only server administrators create and delete projects and
/// assign repositories). Member changes and deletions ask for the password again, and every POST is refused with 503 while the read-only gate is
/// closed (<see cref="AccessControllerBase"/>).
/// </summary>
public sealed class AdminProjectsController : AccessControllerBase
{
    /// <inheritdoc />
    protected override async Task<bool> AuthorizeAsync(CancellationToken cancellationToken)
    {
        var isDetail = ControllerContext.ActionDescriptor.AttributeRouteInfo?.Template?.Contains("{name}") == true;

        return isDetail
            ? await AccessAuthorization.ProjectAsync(AccessAdmin, IsServerAdministrator, CurrentUserId, RouteData.Values["name"]?.ToString(), cancellationToken)
            : await AccessAuthorization.ProjectsAsync(AccessAdmin, IsServerAdministrator, CurrentUserId, cancellationToken);
    }

    /// <summary>Lists the projects the signed-in user may open.</summary>
    [Route("admin/projects"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => ShowList(await Api<AdminProjectsApiController, ProjectsViewModel>(api => api.List(cancellationToken)));

    /// <summary>Creates a project (administrators only) and opens it.</summary>
    [Route("admin/projects"), HttpPost, PageHandler]
    public async Task<IActionResult> Create(string? name, string? displayName, CancellationToken cancellationToken)
        => ShowList(await Api<AdminProjectsApiController, ProjectsViewModel>(api => api.Create(new ProjectCreation(name, displayName), cancellationToken)));

    /// <summary>Shows one project.</summary>
    [Route("admin/projects/{name}"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Detail(string name, CancellationToken cancellationToken)
        => ShowDetail(await Api<AdminProjectsApiController, ProjectDetailViewModel>(api => api.Get(name, cancellationToken)));

    /// <summary>Gives a user or group a role in the project, replacing an earlier one.</summary>
    [Route("admin/projects/{name}"), HttpPost, PageHandler("SetMember")]
    public async Task<IActionResult> SetMember(string name, PrincipalType type, string? principalName, ProjectRole role, string? password, CancellationToken cancellationToken)
        => ShowDetail(await Api<AdminProjectsApiController, ProjectDetailViewModel>(api => api.SetMember(name, new ProjectMemberForm(type, principalName, role, password), cancellationToken)));

    /// <summary>Takes a user or group out of the project.</summary>
    [Route("admin/projects/{name}"), HttpPost, PageHandler("RemoveMember")]
    public async Task<IActionResult> RemoveMember(string name, PrincipalType type, string? principalId, string? password, CancellationToken cancellationToken)
        => ShowDetail(await Api<AdminProjectsApiController, ProjectDetailViewModel>(api => api.RemoveMember(name, new PrincipalRemoval(type, principalId, password), cancellationToken)));

    /// <summary>Puts a repository in the project (server administrators only); it leaves any project it was in.</summary>
    [Route("admin/projects/{name}"), HttpPost, PageHandler("AssignRepository")]
    public async Task<IActionResult> AssignRepository(string name, string? repository, string? password, CancellationToken cancellationToken)
        => ShowDetail(await Api<AdminProjectsApiController, ProjectDetailViewModel>(api => api.AssignRepository(name, new ProjectRepositoryForm(repository, password), cancellationToken)));

    /// <summary>Takes a repository out of the project (server administrators only).</summary>
    [Route("admin/projects/{name}"), HttpPost, PageHandler("UnassignRepository")]
    public async Task<IActionResult> UnassignRepository(string name, string? repository, string? password, CancellationToken cancellationToken)
        => ShowDetail(await Api<AdminProjectsApiController, ProjectDetailViewModel>(api => api.UnassignRepository(name, new ProjectRepositoryForm(repository, password), cancellationToken)));

    /// <summary>Creates a permission target that applies only to the project's repositories.</summary>
    [Route("admin/projects/{name}"), HttpPost, PageHandler("CreateTarget")]
    public async Task<IActionResult> CreateTarget(string name, string? targetName, string? repositoryPatterns, string? includePatterns, string? excludePatterns, string? testPackage, CancellationToken cancellationToken)
        => ShowDetail(await Api<AdminProjectsApiController, ProjectDetailViewModel>(
            api => api.CreateTarget(name, new ProjectTargetForm(targetName, repositoryPatterns, includePatterns, excludePatterns, testPackage), cancellationToken)));

    /// <summary>Previews the new target patterns without saving anything.</summary>
    [Route("admin/projects/{name}"), HttpPost, PageHandler("Preview")]
    public async Task<IActionResult> Preview(string name, string? targetName, string? repositoryPatterns, string? includePatterns, string? excludePatterns, string? testPackage, CancellationToken cancellationToken)
        => ShowDetail(await Api<AdminProjectsApiController, ProjectDetailViewModel>(
            api => api.Preview(name, new ProjectTargetForm(targetName, repositoryPatterns, includePatterns, excludePatterns, testPackage), cancellationToken)));

    /// <summary>Deletes the project (server administrators only) with its members and targets; repositories stay.</summary>
    [Route("admin/projects/{name}"), HttpPost, PageHandler("Delete")]
    public async Task<IActionResult> Delete(string name, string? password, CancellationToken cancellationToken)
        => ShowDetail(await Api<AdminProjectsApiController, ProjectDetailViewModel>(api => api.Delete(name, new PasswordForm(password), cancellationToken)));

    /// <summary>A form posted to the project's own URL with no handler: the screen is rendered (the page had no default handler).</summary>
    [Route("admin/projects/{name}"), HttpPost, PageHandler, ImplicitPageHandler]
    public async Task<IActionResult> DetailPost(string name, CancellationToken cancellationToken)
        => ShowDetail(await Api<AdminProjectsApiController, ProjectDetailViewModel>(api => api.Get(name, cancellationToken)));

    private IActionResult ShowList(UiApiResponse<ProjectsViewModel> response) => RenderFlashed(response, "Index");

    private IActionResult ShowDetail(UiApiResponse<ProjectDetailViewModel> response) => RenderFlashed(response, "Detail");
}
