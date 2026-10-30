using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Identity;

namespace BaGet.Identity.Application.UseCases.Admin;

/// <summary>
/// What the use cases of the administration screens share: the signed-in user, the administrator's password confirmation, the read-only gate and
/// the "go back to" answer. A use case never sees the request: its inputs are the caller (<see cref="ClaimsPrincipal"/>), a form DTO and a cancellation token.
/// </summary>
public abstract class AdminUseCaseBase
{
    /// <summary>The refusal shown when the administrator's password (or recent sign-in) is not right.</summary>
    protected const string NotConfirmed = "The confirmation is incorrect, so nothing was changed.";

    /// <summary>Creates the base.</summary>
    protected AdminUseCaseBase(UserManager<BaGetUser> users, PasswordConfirmation confirmation, MaintenanceGate? gate)
    {
        Users = users ?? throw new ArgumentNullException(nameof(users));
        Confirmation = confirmation ?? throw new ArgumentNullException(nameof(confirmation));
        Gate = gate;
    }

    /// <summary>Users, passwords, tokens.</summary>
    protected UserManager<BaGetUser> Users { get; }

    /// <summary>The password confirmation.</summary>
    protected PasswordConfirmation Confirmation { get; }

    /// <summary>The read-only gate, or null where there is none.</summary>
    protected MaintenanceGate? Gate { get; }

    /// <summary>Refuses (503, <see cref="MaintenanceException"/>) a write while the read-only gate is closed.</summary>
    protected void ThrowIfClosed() => Gate?.ThrowIfClosed();

    /// <summary>The signed-in user, or null (the global API key has no user).</summary>
    protected Task<BaGetUser?> CurrentAsync(ClaimsPrincipal caller) => Users.GetUserAsync(caller)!;

    /// <summary>Route values for a <see cref="ScreenNext"/>; a null value is left out of the link.</summary>
    protected static IReadOnlyDictionary<string, object?> Route(params (string Name, object? Value)[] values)
    {
        var route = new Dictionary<string, object?>(values.Length);
        foreach (var (name, value) in values) route[name] = value;

        return route;
    }

    /// <summary>The action is over: go to <paramref name="screen"/> and show the message or the error there (the flash message).</summary>
    protected static ScreenResult<T> Back<T>(ScreenRef screen, IReadOnlyDictionary<string, object?>? route, string? message, string? error)
        => ScreenResult<T>.GoTo(new ScreenNext(screen, route, message, error));
}
