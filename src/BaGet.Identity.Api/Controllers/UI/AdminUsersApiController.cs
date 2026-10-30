using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Packages;
using BaGet.Identity.Application.Models.Shared;
using BaGet.Identity.Application.Models.Users;
using BaGet.Identity.Application.UseCases.Admin;
using BaGet.Identity.Application.UseCases.Packages;

namespace BaGet.Identity.Controllers.UI;

/// <summary>
/// The JSON twin of the administrator's account screens (<c>/api/ui/admin/users</c>): the user list and its actions, one account
/// (<c>/api/ui/admin/users/{name}</c>), its deletion and the packages it owns. <c>AdminUsersController</c> calls it in process and renders the
/// views. Administrators only (a session, or an <c>admin:api</c> key); every write asks the administrator's password in the body and is refused with 503
/// while the server is read-only. A password a write generates (reset, new account) is in the answer once, with <c>Cache-Control: no-store</c>.
/// </summary>
[Route("api/ui/admin/users")]
public sealed class AdminUsersApiController : AdminApiControllerBase
{
    // Resolved in the action, after the gate: the use cases exist only while accounts are enabled.
    private AdminUsersUseCase Users => UseCase<AdminUsersUseCase>();

    private AdminUserDetailUseCase Detail => UseCase<AdminUserDetailUseCase>();

    private AdminUserDeletionUseCase Deletion => UseCase<AdminUserDeletionUseCase>();

    private AdminPackagesUseCase Packages => UseCase<AdminPackagesUseCase>();

    // ---------------------------------------------------------------- Users

    /// <summary>Lists the users, filtered by <paramref name="q"/> (user name contains), one page of them.</summary>
    [HttpGet(""), UiApi("AdminUsers", "Index")]
    public async Task<ActionResult<ScreenResult<UsersViewModel>>> List(string? q, [FromQuery(Name = "p")] int pageNumber = 1, CancellationToken cancellationToken = default)
        => Respond(await Users.GetAsync(User, q, pageNumber, cancellationToken));

    /// <summary>Enables a self-registered account that waits for approval.</summary>
    [HttpPost("approve"), UiApi("AdminUsers", "Index", Handler = "Approve")]
    public async Task<ActionResult<ScreenResult<UsersViewModel>>> Approve([FromBody] UserChange? change, CancellationToken cancellationToken = default)
        => Respond(await Users.ApproveAsync(User, change ?? new UserChange(null, null), cancellationToken));

    /// <summary>Makes a user an administrator.</summary>
    [HttpPost("promote"), UiApi("AdminUsers", "Index", Handler = "Promote")]
    public async Task<ActionResult<ScreenResult<UsersViewModel>>> Promote([FromBody] UserChange? change, CancellationToken cancellationToken = default)
        => Respond(await Users.PromoteAsync(User, change ?? new UserChange(null, null), cancellationToken));

    /// <summary>Removes a user's administrator flag.</summary>
    [HttpPost("demote"), UiApi("AdminUsers", "Index", Handler = "Demote")]
    public async Task<ActionResult<ScreenResult<UsersViewModel>>> Demote([FromBody] UserChange? change, CancellationToken cancellationToken = default)
        => Respond(await Users.DemoteAsync(User, change ?? new UserChange(null, null), cancellationToken));

    /// <summary>Replaces a user's password with a generated one, which the answer carries once (<c>generatedPassword</c>).</summary>
    [HttpPost("reset-password"), UiApi("AdminUsers", "Index", Handler = "ResetPassword")]
    public async Task<ActionResult<ScreenResult<UsersViewModel>>> ResetPassword([FromBody] UserChange? change, CancellationToken cancellationToken = default)
        => Shown(await Users.ResetPasswordAsync(User, change ?? new UserChange(null, null), cancellationToken));

    /// <summary>Mails a user a password reset link.</summary>
    [HttpPost("send-reset-link"), UiApi("AdminUsers", "Index", Handler = "SendResetLink")]
    public async Task<ActionResult<ScreenResult<UsersViewModel>>> SendResetLink([FromBody] UserChange? change, CancellationToken cancellationToken = default)
        => Respond(await Users.SendResetLinkAsync(User, change ?? new UserChange(null, null), cancellationToken));

    /// <summary>Creates an account (typed, generated or invited password). A generated password is in the answer once.</summary>
    [HttpPost("create"), UiApi("AdminUsers", "Index", Handler = "Create")]
    public async Task<ActionResult<ScreenResult<UsersViewModel>>> Create([FromBody] UserCreation? creation, CancellationToken cancellationToken = default)
        => Shown(await Users.CreateAsync(User, creation ?? new UserCreation(null, null, null, false, null), cancellationToken));

    // A password that is shown once must never be cached by the browser or a proxy.
    private ActionResult<ScreenResult<UsersViewModel>> Shown(ScreenResult<UsersViewModel> result)
    {
        if (result.Model?.GeneratedPassword != null) Response.Headers.CacheControl = "no-store";

        return Respond(result);
    }

    // ---------------------------------------------------------------- UserDetail

    /// <summary>Shows one account. <paramref name="package"/> is a package ID to evaluate the effective permissions for.</summary>
    [HttpGet("{name}"), UiApi("AdminUsers", "Detail")]
    public async Task<ActionResult<ScreenResult<UserDetailViewModel>>> Get(string name, string? package, CancellationToken cancellationToken = default)
        => Respond(await Detail.GetAsync(User, name, package, cancellationToken));

    /// <summary>Enables the account when it waits for approval.</summary>
    [HttpPost("{name}/approve"), UiApi("AdminUsers", "Detail", Handler = "Approve")]
    public async Task<ActionResult<ScreenResult<UserDetailViewModel>>> ApproveAccount(string name, [FromBody] PasswordForm? form, CancellationToken cancellationToken = default)
        => Respond(await Detail.ApproveAsync(User, name, form?.Password, cancellationToken));

    /// <summary>Revokes one of the account's API keys.</summary>
    [HttpPost("{name}/revoke-key"), UiApi("AdminUsers", "Detail", Handler = "RevokeKey")]
    public async Task<ActionResult<ScreenResult<UserDetailViewModel>>> RevokeKey(string name, [FromBody] UserKeyForm? form, CancellationToken cancellationToken = default)
        => Respond(await Detail.RevokeKeyAsync(User, name, form?.Key ?? 0, form?.Password, cancellationToken));

    /// <summary>Adds the account to a group.</summary>
    [HttpPost("{name}/add-group"), UiApi("AdminUsers", "Detail", Handler = "AddGroup")]
    public async Task<ActionResult<ScreenResult<UserDetailViewModel>>> AddGroup(string name, [FromBody] UserGroupForm? form, CancellationToken cancellationToken = default)
        => Respond(await Detail.AddGroupAsync(User, name, form?.Group, form?.Password, cancellationToken));

    /// <summary>Removes the account from a group.</summary>
    [HttpPost("{name}/remove-group"), UiApi("AdminUsers", "Detail", Handler = "RemoveGroup")]
    public async Task<ActionResult<ScreenResult<UserDetailViewModel>>> RemoveGroup(string name, [FromBody] UserGroupForm? form, CancellationToken cancellationToken = default)
        => Respond(await Detail.RemoveGroupAsync(User, name, form?.Group, form?.Password, cancellationToken));

    // ---------------------------------------------------------------- DeleteUser

    /// <summary>Shows what deleting the account does.</summary>
    [HttpGet("{name}/delete"), UiApi("AdminUsers", "DeleteUser")]
    public async Task<ActionResult<ScreenResult<DeleteUserViewModel>>> GetDeletion(string name, CancellationToken cancellationToken = default)
        => Respond(await Deletion.GetAsync(User, name, cancellationToken));

    /// <summary>Deletes the account after the administrator's password.</summary>
    [HttpPost("{name}/delete"), UiApi("AdminUsers", "DeleteUser")]
    public async Task<ActionResult<ScreenResult<DeleteUserViewModel>>> Delete(string name, [FromBody] UserDeletionForm? form, CancellationToken cancellationToken = default)
        => Respond(await Deletion.DeleteAsync(User, name, form?.SoleOwned, form?.Password, cancellationToken));

    // ---------------------------------------------------------------- UserPackages

    /// <summary>Lists the package IDs one user owns.</summary>
    [HttpGet("{name}/packages"), UiApi("AdminUsers", "UserPackages"), RepositoryPage]
    public async Task<ActionResult<ScreenResult<UserPackagesViewModel>>> GetPackages(string name, CancellationToken cancellationToken = default)
        => Respond(await Packages.GetUserAsync(name, cancellationToken));
}
