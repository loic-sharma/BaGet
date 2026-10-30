using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using BaGet.Web.V3.Api.Filters;
using BaGet.Web.V3.Models.Api;
using BaGet.Web.V3.Api;

namespace BaGet.Web.V3.Controllers.Base;

/// <summary>
/// Base of the administration REST API (<c>/api/v1</c>): <see cref="AdminApiAuthorizeAttribute"/> and
/// <see cref="ApiExceptionFilterAttribute"/> apply to every action, results are JSON written with <see cref="ApiJson.Options"/>, and
/// <see cref="RequireConfirmationAsync"/> is the API's form of the page's "type your password again".
/// </summary>
[ApiController]
[AdminApiAuthorize]
[ApiExceptionFilter]
public abstract class AdminApiController : ControllerBase
{
    /// <summary>The header that carries the key owner's password for an operation whose page asks for it.</summary>
    public const string ConfirmHeader = "X-Confirm-Password";

    /// <summary>The authenticated caller.</summary>
    protected BaGetPrincipal Caller => (BaGetPrincipal)HttpContext.Items[AdminApiAuthorizeAttribute.CallerItem]!;

    /// <summary>The name recorded in audit entries: the key owner, or <c>(global key)</c>.</summary>
    protected string Actor => Caller.UserName ?? "(global key)";

    /// <summary>Cancelled when the caller goes away.</summary>
    protected CancellationToken Cancellation => HttpContext.RequestAborted;

    /// <summary>A service of the request scope.</summary>
    protected T Service<T>() where T : notnull => HttpContext.RequestServices.GetRequiredService<T>();

    /// <summary>Fails with 503 while the server is read-only, as a page's POST does.</summary>
    protected void ThrowIfClosed() => HttpContext.RequestServices.GetService<MaintenanceGate>()?.ThrowIfClosed();

    /// <summary>A JSON answer with <paramref name="status"/>.</summary>
    protected IActionResult Respond(object? body, int status = 200, string? location = null)
    {
        if (location != null) Response.Headers[HeaderNames.Location] = location;

        return new JsonResult(body, ApiJson.Options) { StatusCode = status };
    }

    /// <summary>One page of <paramref name="all"/>.</summary>
    protected IActionResult RespondPage<T>(IReadOnlyList<T> all, int? page, int? pageSize)
    {
        var (number, size) = ApiProblems.Clamp(page, pageSize);

        return Respond(new Paged<T>(all.Skip((number - 1) * size).Take(size).ToList(), number, size, all.Count));
    }

    /// <summary>204.</summary>
    protected IActionResult Done() => new StatusCodeResult(204);

    /// <summary>
    /// The password confirmation of an operation whose page asks for the password: the key owner's password in
    /// <see cref="ConfirmHeader"/>, checked as the page does (a locked-out account is refused, a wrong password counts as a failed
    /// sign-in). The global key has no password: the key itself is the confirmation. An owner without a password (external sign-in
    /// only) has nothing to type, so the operation is refused with a pointer to set one.
    /// </summary>
    /// <returns>Null when confirmed, else the answer to send (nothing was changed).</returns>
    protected async Task<IActionResult?> RequireConfirmationAsync()
    {
        if (Caller.IsSuperuser) return null;

        var users = HttpContext.RequestServices.GetService<UserManager<BaGetUser>>();
        var user = users == null || Caller.UserId == null ? null : await users.FindByIdAsync(Caller.UserId);
        if (users == null || user == null)
        {
            return ApiProblems.Create(403, "confirmation-unavailable", "Confirmation unavailable", "The key's owner could not be found.");
        }

        if (!await users.HasPasswordAsync(user))
        {
            return ApiProblems.Create(403, "confirmation-unavailable", "Confirmation unavailable", "This operation asks for the owner's password and the account has none (it signs in with an external provider). Set a password on the account page first.");
        }

        if (await users.IsLockedOutAsync(user))
        {
            return ApiProblems.Create(403, "locked-out", "Account locked out", "The account is locked out after failed sign-ins. Try again later.");
        }

        var password = Request.Headers[ConfirmHeader].ToString();
        if (string.IsNullOrEmpty(password) || !await users.CheckPasswordAsync(user, password))
        {
            if (!string.IsNullOrEmpty(password)) await users.AccessFailedAsync(user);

            return ApiProblems.Create(403, "confirmation-required", "Confirmation required", $"Send the key owner's password in the {ConfirmHeader} header. The confirmation is incorrect, so nothing was changed.");
        }

        return null;
    }
}
