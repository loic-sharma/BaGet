using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using BaGet.Identity.Application.Models.Access;
using BaGet.Identity.Application.Models.Shared;
using BaGet.Identity.Application.UseCases.Access;

namespace BaGet.Identity.Controllers.UI;

/// <summary>
/// The JSON twin of the permission target screens (<c>/api/ui/admin/permissions</c>): the list with the new-target form (server administrators only)
/// and one target (patterns, preview, grants, delete), which also admits the administrators of the target's project for a project target (the target
/// is the <c>name</c> route value). <c>AdminPermissionsController</c> calls it in process. Saving patterns, changing grants and deleting ask for
/// the password in the body; every write is refused with 503 while the server is read-only.
/// </summary>
[Route("api/ui/admin/permissions")]
public sealed class AdminPermissionsApiController : AccessApiControllerBase
{
    private static readonly ScreenRef DetailScreen = ScreenCatalog.AdminPermissionDetail;

    private AdminPermissionsUseCase Permissions => UseCase<AdminPermissionsUseCase>();

    /// <inheritdoc/>
    protected override async Task<bool> AuthorizeAsync(CancellationToken cancellationToken)
    {
        // Only the detail screen is widened to a project's administrators (the target is the name route value).
        if (CurrentScreen() != DetailScreen) return false;

        var services = HttpContext.RequestServices;

        return await AccessAuthorization.TargetAsync(
            services.GetRequiredService<IAccessAdministration>(),
            false,
            services.GetRequiredService<UserManager<BaGetUser>>().GetUserId(User),
            RouteData.Values["name"]?.ToString(),
            cancellationToken);
    }

    /// <summary>Lists the permission targets.</summary>
    [HttpGet(""), UiApi("AdminPermissions", "Index")]
    public async Task<ActionResult<ScreenResult<PermissionsViewModel>>> List(CancellationToken cancellationToken = default)
        => Respond(await Permissions.ListAsync(await CallerAsync(cancellationToken), cancellationToken));

    /// <summary>Creates a target and points at it.</summary>
    [HttpPost(""), UiApi("AdminPermissions", "Index")]
    public async Task<ActionResult<ScreenResult<PermissionsViewModel>>> Create([FromBody] PermissionTargetForm? form, CancellationToken cancellationToken = default)
        => Respond(await Permissions.CreateAsync(await CallerAsync(cancellationToken), form ?? new PermissionTargetForm(), cancellationToken));

    /// <summary>Previews the form patterns without saving anything.</summary>
    [HttpPost("preview"), UiApi("AdminPermissions", "Index", Handler = "Preview")]
    public async Task<ActionResult<ScreenResult<PermissionsViewModel>>> Preview([FromBody] PermissionTargetForm? form, CancellationToken cancellationToken = default)
        => Respond(await Permissions.PreviewAsync(await CallerAsync(cancellationToken), form ?? new PermissionTargetForm(), cancellationToken));

    /// <summary>Re-creates the built-in groups and targets that are missing (the upgrade defaults).</summary>
    [HttpPost("restore-built-ins"), UiApi("AdminPermissions", "Index", Handler = "RestoreBuiltIns")]
    public async Task<ActionResult<ScreenResult<PermissionsViewModel>>> RestoreBuiltIns([FromBody] PasswordForm? form, CancellationToken cancellationToken = default)
        => Respond(await Permissions.RestoreBuiltInsAsync(await CallerAsync(cancellationToken), form ?? new PasswordForm(null), cancellationToken));

    /// <summary>Shows one target. <paramref name="principalType"/> and <paramref name="principalName"/> pre-fill the grant form.</summary>
    [HttpGet("{name}"), UiApi("AdminPermissions", "Detail")]
    public async Task<ActionResult<ScreenResult<PermissionDetailViewModel>>> Get(string name, string? testPackage, PrincipalType? principalType, string? principalName, CancellationToken cancellationToken = default)
        => Respond(await Permissions.GetAsync(await CallerAsync(cancellationToken), name, testPackage, principalType, principalName, cancellationToken));

    /// <summary>Previews the form patterns (and the test package) without saving anything.</summary>
    [HttpPost("{name}/preview"), UiApi("AdminPermissions", "Detail", Handler = "Preview")]
    public async Task<ActionResult<ScreenResult<PermissionDetailViewModel>>> PreviewDetail(string name, [FromBody] PermissionTargetForm? form, CancellationToken cancellationToken = default)
        => Respond(await Permissions.PreviewDetailAsync(await CallerAsync(cancellationToken), name, form ?? new PermissionTargetForm(), cancellationToken));

    /// <summary>Replaces the target's patterns.</summary>
    [HttpPost("{name}/save"), UiApi("AdminPermissions", "Detail", Handler = "Save")]
    public async Task<ActionResult<ScreenResult<PermissionDetailViewModel>>> Save(string name, [FromBody] PermissionTargetForm? form, CancellationToken cancellationToken = default)
        => Respond(await Permissions.SaveAsync(await CallerAsync(cancellationToken), name, form ?? new PermissionTargetForm(), cancellationToken));

    /// <summary>Gives a principal the ticked actions through the target, replacing an earlier grant to it.</summary>
    [HttpPost("{name}/set-grant"), UiApi("AdminPermissions", "Detail", Handler = "SetGrant")]
    public async Task<ActionResult<ScreenResult<PermissionDetailViewModel>>> SetGrant(string name, [FromBody] GrantForm? form, CancellationToken cancellationToken = default)
        => Respond(await Permissions.SetGrantAsync(await CallerAsync(cancellationToken), name, form ?? new GrantForm(null, null, null, null), cancellationToken));

    /// <summary>Removes a principal's grant.</summary>
    [HttpPost("{name}/remove-grant"), UiApi("AdminPermissions", "Detail", Handler = "RemoveGrant")]
    public async Task<ActionResult<ScreenResult<PermissionDetailViewModel>>> RemoveGrant(string name, [FromBody] PrincipalRemoval? form, CancellationToken cancellationToken = default)
        => Respond(await Permissions.RemoveGrantAsync(await CallerAsync(cancellationToken), name, form ?? new PrincipalRemoval(PrincipalType.Group, null, null), cancellationToken));

    /// <summary>Deletes the target and its grants.</summary>
    [HttpPost("{name}/delete"), UiApi("AdminPermissions", "Detail", Handler = "Delete")]
    public async Task<ActionResult<ScreenResult<PermissionDetailViewModel>>> Delete(string name, [FromBody] PasswordForm? form, CancellationToken cancellationToken = default)
        => Respond(await Permissions.DeleteAsync(await CallerAsync(cancellationToken), name, form ?? new PasswordForm(null), cancellationToken));
}
