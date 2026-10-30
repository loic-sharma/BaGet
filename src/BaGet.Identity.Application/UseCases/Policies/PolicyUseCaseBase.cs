using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Identity;
using BaGet.Identity.Application.UseCases.Admin;

namespace BaGet.Identity.Application.UseCases.Policies;

/// <summary>
/// What the use cases of the retention, scanning and replication screens share: one change that asks the administrator's password,
/// turns an <see cref="AccessModelException"/> into the refusal shown on the screen the action comes back to, and says where to go afterwards.
/// </summary>
public abstract class PolicyUseCaseBase : AdminUseCaseBase
{
    /// <summary>Creates the base.</summary>
    protected PolicyUseCaseBase(UserManager<BaGetUser> users, PasswordConfirmation confirmation, MaintenanceGate? gate)
        : base(users, confirmation, gate)
    {
    }

    /// <summary>The user name recorded in the audit log.</summary>
    protected static string? ActorOf(ClaimsPrincipal caller) => caller.Identity?.Name;

    /// <summary>
    /// Runs one change: refused with 503 while the server is read-only, asks for the password when <paramref name="confirm"/> (the answer is the
    /// refusal on <paramref name="screen"/>), turns an <see cref="AccessModelException"/> into the refusal shown there, else shows the message
    /// <paramref name="change"/> returns.
    /// </summary>
    protected async Task<ScreenResult<T>> ActAsync<T>(
        ClaimsPrincipal caller,
        string? password,
        bool confirm,
        Func<Task<string>> change,
        ScreenRef screen,
        IReadOnlyDictionary<string, object?>? route)
    {
        ThrowIfClosed();

        if (confirm)
        {
            var current = await CurrentAsync(caller);
            if (current == null) return ScreenResult<T>.SignIn();

            if (!await Confirmation.IsConfirmedAsync(current, password)) return Back<T>(screen, route, null, NotConfirmed);
        }

        try
        {
            return Back<T>(screen, route, await change(), null);
        }
        catch (AccessModelException e)
        {
            return Back<T>(screen, route, null, e.Message);
        }
    }
}
