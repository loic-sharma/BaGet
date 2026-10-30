using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Identity.Controllers.UI;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Packages;
using BaGet.Identity.Application.Models.Shared;
using BaGet.Identity.Application.Models.Users;
using BaGet.Identity.Application.UseCases.Admin;

namespace BaGet.Identity.Controllers.FrontEnd;

/// <summary>
/// The administrator's account screens: the user list (<c>/admin/users</c>), one account (<c>/admin/users/{name}</c>), its deletion
/// (<c>/admin/users/{name}/delete</c>) and the packages it owns (<c>/admin/users/{name}/packages</c>). They replace the Razor Pages
/// <c>/Admin/Users</c>, <c>/Admin/UserDetail</c>, <c>/Admin/DeleteUser</c> and <c>/Admin/UserPackages</c> (same URLs, same
/// <c>?handler=</c> values, ); the work is done by the JSON twin
/// (<see cref="AdminUsersApiController"/>, over the Application project's use cases) and this controller only renders its answers.
/// Administrators only (<see cref="AdminControllerBase"/>); every action asks the administrator's password again and is refused with 503
/// while the read-only gate is closed, as the pages did.
/// </summary>
public sealed class AdminUsersController : AdminControllerBase
{
    // ---------------------------------------------------------------- Users

    /// <summary>Lists the users.</summary>
    [Route("admin/users"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Index(string? q, [ModelBinder(Name = "p")] int pageNumber = 1, CancellationToken cancellationToken = default)
        => RenderFlashed(await Api<AdminUsersApiController, UsersViewModel>(api => api.List(q, pageNumber, cancellationToken)), "Index");

    /// <summary>Enables a self-registered account that waits for approval.</summary>
    [Route("admin/users"), HttpPost, PageHandler("Approve")]
    public async Task<IActionResult> Approve(string userName, string? password, string? q, [ModelBinder(Name = "p")] int pageNumber = 1, CancellationToken cancellationToken = default)
        => RenderFlashed(await Api<AdminUsersApiController, UsersViewModel>(api => api.Approve(new UserChange(userName, password, q, pageNumber), cancellationToken)), "Index");

    /// <summary>Makes a user an administrator.</summary>
    [Route("admin/users"), HttpPost, PageHandler("Promote")]
    public async Task<IActionResult> Promote(string userName, string? password, string? q, [ModelBinder(Name = "p")] int pageNumber = 1, CancellationToken cancellationToken = default)
        => RenderFlashed(await Api<AdminUsersApiController, UsersViewModel>(api => api.Promote(new UserChange(userName, password, q, pageNumber), cancellationToken)), "Index");

    /// <summary>Removes a user's administrator flag.</summary>
    [Route("admin/users"), HttpPost, PageHandler("Demote")]
    public async Task<IActionResult> Demote(string userName, string? password, string? q, [ModelBinder(Name = "p")] int pageNumber = 1, CancellationToken cancellationToken = default)
        => RenderFlashed(await Api<AdminUsersApiController, UsersViewModel>(api => api.Demote(new UserChange(userName, password, q, pageNumber), cancellationToken)), "Index");

    /// <summary>Replaces a user's password with a generated one and shows it once (the response is the screen, not a redirect).</summary>
    [Route("admin/users"), HttpPost, PageHandler("ResetPassword")]
    public async Task<IActionResult> ResetPassword(string userName, string? password, string? resetEmail, string? q, [ModelBinder(Name = "p")] int pageNumber = 1, CancellationToken cancellationToken = default)
        => RenderFlashed(await Api<AdminUsersApiController, UsersViewModel>(api => api.ResetPassword(new UserChange(userName, password, q, pageNumber, resetEmail), cancellationToken)), "Index");

    /// <summary>Mails a user a password reset link.</summary>
    [Route("admin/users"), HttpPost, PageHandler("SendResetLink")]
    public async Task<IActionResult> SendResetLink(string userName, string? password, string? q, [ModelBinder(Name = "p")] int pageNumber = 1, CancellationToken cancellationToken = default)
        => RenderFlashed(await Api<AdminUsersApiController, UsersViewModel>(api => api.SendResetLink(new UserChange(userName, password, q, pageNumber), cancellationToken)), "Index");

    /// <summary>Creates an account (typed, generated or invited password).</summary>
    [Route("admin/users"), HttpPost, PageHandler("Create")]
    public async Task<IActionResult> Create(
        string? newUserName,
        string? newEmail,
        string? newPassword,
        bool invite,
        string? password,
        string? q,
        [ModelBinder(Name = "p")] int pageNumber = 1,
        CancellationToken cancellationToken = default)
        => RenderFlashed(
            await Api<AdminUsersApiController, UsersViewModel>(api => api.Create(new UserCreation(newUserName, newEmail, newPassword, invite, password, q, pageNumber), cancellationToken)),
            "Index");

    /// <summary>A form posted to the screen's own URL, with no handler: the screen is rendered (the page had no default handler).</summary>
    [Route("admin/users"), HttpPost, PageHandler, ImplicitPageHandler]
    public async Task<IActionResult> IndexPost(string? q, [ModelBinder(Name = "p")] int pageNumber = 1, CancellationToken cancellationToken = default)
        => RenderFlashed(await Api<AdminUsersApiController, UsersViewModel>(api => api.List(q, pageNumber, cancellationToken)), "Index");

    // ---------------------------------------------------------------- UserDetail

    /// <summary>Shows one account.</summary>
    [Route("admin/users/{name}"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Detail(string name, string? package, CancellationToken cancellationToken)
        => RenderFlashed(await Api<AdminUsersApiController, UserDetailViewModel>(api => api.Get(name, package, cancellationToken)), "Detail");

    /// <summary>Enables the account when it waits for approval.</summary>
    [Route("admin/users/{name}"), HttpPost, PageHandler("Approve")]
    public async Task<IActionResult> DetailApprove(string name, string? package, string? password, CancellationToken cancellationToken)
        => RenderFlashed(await Api<AdminUsersApiController, UserDetailViewModel>(api => api.ApproveAccount(name, new PasswordForm(password), cancellationToken)), "Detail");

    /// <summary>Revokes one of the account's API keys.</summary>
    [Route("admin/users/{name}"), HttpPost, PageHandler("RevokeKey")]
    public async Task<IActionResult> RevokeKey(string name, string? package, int key, string? password, CancellationToken cancellationToken)
        => RenderFlashed(await Api<AdminUsersApiController, UserDetailViewModel>(api => api.RevokeKey(name, new UserKeyForm(key, password), cancellationToken)), "Detail");

    /// <summary>Adds the account to a group.</summary>
    [Route("admin/users/{name}"), HttpPost, PageHandler("AddGroup")]
    public async Task<IActionResult> AddGroup(string name, string? package, string? group, string? password, CancellationToken cancellationToken)
        => RenderFlashed(await Api<AdminUsersApiController, UserDetailViewModel>(api => api.AddGroup(name, new UserGroupForm(group, password), cancellationToken)), "Detail");

    /// <summary>Removes the account from a group.</summary>
    [Route("admin/users/{name}"), HttpPost, PageHandler("RemoveGroup")]
    public async Task<IActionResult> RemoveGroup(string name, string? package, string? group, string? password, CancellationToken cancellationToken)
        => RenderFlashed(await Api<AdminUsersApiController, UserDetailViewModel>(api => api.RemoveGroup(name, new UserGroupForm(group, password), cancellationToken)), "Detail");

    /// <summary>A form posted to the screen's own URL, with no handler: the screen is rendered (the page had no default handler).</summary>
    [Route("admin/users/{name}"), HttpPost, PageHandler, ImplicitPageHandler]
    public async Task<IActionResult> DetailPost(string name, string? package, CancellationToken cancellationToken)
        => RenderFlashed(await Api<AdminUsersApiController, UserDetailViewModel>(api => api.Get(name, package, cancellationToken)), "Detail");

    // ---------------------------------------------------------------- DeleteUser

    /// <summary>Shows what deleting the account does.</summary>
    [Route("admin/users/{name}/delete"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> DeleteUser(string name, CancellationToken cancellationToken)
        => Render(await Api<AdminUsersApiController, DeleteUserViewModel>(api => api.GetDeletion(name, cancellationToken)), "Delete");

    /// <summary>Deletes the account after the administrator's password.</summary>
    [Route("admin/users/{name}/delete"), HttpPost, PageHandler]
    public async Task<IActionResult> DeleteUserConfirmed(string name, string? soleOwned, string? password, CancellationToken cancellationToken)
        => Render(await Api<AdminUsersApiController, DeleteUserViewModel>(api => api.Delete(name, new UserDeletionForm(soleOwned, password), cancellationToken)), "Delete");

    // ---------------------------------------------------------------- UserPackages

    /// <summary>Lists the package IDs one user owns.</summary>
    [Route("admin/users/{name}/packages"), HttpGet, HttpHead, PageHandler, RepositoryPage]
    public async Task<IActionResult> UserPackages(string name, CancellationToken cancellationToken)
        => Render(await Api<AdminUsersApiController, UserPackagesViewModel>(api => api.GetPackages(name, cancellationToken)), "Packages");
}
