using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Services.Sessions;
using BaGet.Identity.Application.UseCases.Account;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaGet.Identity.Application.Security;

/// <summary>
/// The sign-in manager, made to work while the <see cref="MaintenanceGate"/> is closed.
/// </summary>
/// <remarks>
/// A successful sign-in only needs to read the database, but Identity also writes the user on the way: it resets the
/// failed-attempt counter. With the gate closed that write is refused (503), which would keep an administrator who is
/// not signed in from reaching the Settings page after a database copy, where the status and the restart instructions
/// are. The rule is: while the gate is closed a sign-in completes without its incidental, non-essential writes (here, the
/// counter reset, which is retried by the next sign-in once the gate is open); every other write stays refused, a wrong
/// password included, because counting the failed attempt is a write.
/// </remarks>
public class MaintenanceAwareSignInManager : SignInManager<BaGetUser>
{
    private readonly MaintenanceGate _gate;

    public MaintenanceAwareSignInManager(
        UserManager<BaGetUser> userManager,
        IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<BaGetUser> claimsFactory,
        IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<BaGetUser>> logger,
        IAuthenticationSchemeProvider schemes,
        IUserConfirmation<BaGetUser> confirmation,
        MaintenanceGate gate)
        : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
    {
        _gate = gate;
    }

    /// <summary>
    /// An account awaiting an administrator's approval (<see cref="BaGetUser.PendingApproval"/>) cannot sign in by any
    /// route (password, two-factor, external provider): every one of them asks here first, and the result is the
    /// same "not allowed" as an unconfirmed email.
    /// </summary>
    public override async Task<bool> CanSignInAsync(BaGetUser user)
        => !user.PendingApproval && await base.CanSignInAsync(user);

    /// <summary>
    /// For an account that may not sign in yet (awaiting approval, or an unconfirmed email) Identity answers "not allowed" before it looks at the password or the lockout,
    /// which would let anyone guess passwords without limit and learn from the answer which one is right. Here the
    /// lockout is honoured first (a locked-out account answers "locked out" whatever the password), a wrong password
    /// is a counted failed attempt (a write, so refused while the gate is closed like any failed sign-in), and only
    /// the right one answers "not allowed". Nothing about the password is learned from a locked-out account, and the existence of an
    /// unconfirmed account is not revealed to a wrong password.
    /// </summary>
    public override async Task<SignInResult> CheckPasswordSignInAsync(BaGetUser user, string password, bool lockoutOnFailure)
    {
        if (user == null || await CanSignInAsync(user)) return await base.CheckPasswordSignInAsync(user!, password, lockoutOnFailure);

        if (await UserManager.IsLockedOutAsync(user)) return SignInResult.LockedOut;

        if (await UserManager.CheckPasswordAsync(user, password)) return SignInResult.NotAllowed;

        if (UserManager.SupportsUserLockout && lockoutOnFailure)
        {
            // Like Identity: an attempt that could not be recorded (a concurrent update) is a plain failure.
            var counted = await UserManager.AccessFailedAsync(user);
            if (counted.Succeeded && await UserManager.IsLockedOutAsync(user)) return SignInResult.LockedOut;
        }

        return SignInResult.Failed;
    }

    /// <summary>
    /// Keeps the current session signed in after a change of the security stamp. A cookie session gets a new cookie. A token session (<c>/api/ui</c> with a
    /// session token) has no cookie: it gets a renewed pair of tokens in the response headers (<see cref="SessionHeaders"/>), with the same authentication time.
    /// </summary>
    public override async Task RefreshSignInAsync(BaGetUser user)
    {
        if (Context != null && Context.Items.TryGetValue(AccountConfirmation.SessionAuthenticatedItem, out var signedIn) && signedIn is System.DateTime authenticatedAt)
        {
            // Only the caller's own account gets tokens (a stamp change of another account, an administrator's action, renews nothing).
            var session = Context.RequestServices.GetService<SessionUseCase>();
            if (session != null && Context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value == user.Id)
            {
                var pair = await session.IssueAsync(user, authenticatedAt);
                Context.Response.Headers[SessionHeaders.AccessToken] = pair.AccessToken;
                Context.Response.Headers[SessionHeaders.AccessExpires] = pair.ExpiresAt?.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
                Context.Response.Headers[SessionHeaders.RefreshToken] = pair.RefreshToken;
                Context.Response.Headers[SessionHeaders.RefreshExpires] = pair.RefreshExpiresAt?.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
                Context.Response.Headers[Microsoft.Net.Http.Headers.HeaderNames.CacheControl] = "no-store";
            }

            return;
        }

        await base.RefreshSignInAsync(user);
    }

    /// <summary>Resets the failed-attempt counter, unless the gate is closed (a write it would refuse).</summary>
    protected override Task ResetLockout(BaGetUser user)
        => _gate.IsClosed ? Task.CompletedTask : base.ResetLockout(user);
}
