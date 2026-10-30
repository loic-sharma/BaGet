using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Identity;
using BaGet.Identity.Application.Models.Policies;
using BaGet.Identity.Application.Models.Shared;
using BaGet.Identity.Application.UseCases.Admin;

namespace BaGet.Identity.Application.UseCases.Access;

/// <summary>
/// What the use cases of the access-control screens (groups, permission targets, projects, audit) share: the caller, the password confirmation of a
/// change, the audit actor and the conversion of an <see cref="AccessModelException"/> into the message shown on the screen. The gate that runs first
/// has already decided who may use the screen (an administrator, or a project's administrator, see <see cref="AccessAuthorization"/>); everything else
/// (password, the 404 and administrator-only rules of each action, the audit through <see cref="IAccessAdministration"/>) is in the use case.
/// </summary>
public abstract class AccessUseCaseBase : AdminUseCaseBase
{
    /// <summary>Creates the base.</summary>
    protected AccessUseCaseBase(
        UserManager<BaGetUser> users,
        PasswordConfirmation confirmation,
        IAccessAdministration access,
        IRepositoryService repositories,
        MaintenanceGate? gate)
        : base(users, confirmation, gate)
    {
        Access = access ?? throw new ArgumentNullException(nameof(access));
        Repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
    }

    /// <summary>The access model's administration.</summary>
    protected IAccessAdministration Access { get; }

    /// <summary>The repositories.</summary>
    protected IRepositoryService Repositories { get; }

    /// <summary>The user name recorded in the audit log.</summary>
    protected static string? Actor(AccessCaller caller) => caller.Principal.Identity?.Name;

    /// <summary>The caller's user id, or null (the global API key has no account).</summary>
    protected string? UserIdOf(AccessCaller caller) => Users.GetUserId(caller.Principal);

    /// <summary>Fills what the confirmation partial needs; the page to come back to after signing in again is the current request's URL.</summary>
    protected Task LoadConfirmationAsync(ConfirmationModel model, AccessCaller caller)
        => Confirmation.LoadForRequestAsync(model, caller.Principal);

    /// <summary>
    /// Runs one change: refuses it while the server is read-only, asks for the password when <paramref name="confirm"/>, turns an
    /// <see cref="AccessModelException"/> into the error shown, then answers with <paramref name="back"/> (given the message and the error).
    /// </summary>
    /// <param name="caller">Who changes.</param>
    /// <param name="password">What the user typed.</param>
    /// <param name="confirm">Whether the change asks for the password (grants, memberships, deletions).</param>
    /// <param name="change">The change; returns the message to show.</param>
    /// <param name="back">Where to go afterwards.</param>
    protected async Task<ScreenResult<T>> ActAsync<T>(
        AccessCaller caller,
        string? password,
        bool confirm,
        Func<Task<string>> change,
        Func<string?, string?, ScreenResult<T>> back)
    {
        ThrowIfClosed();

        if (confirm)
        {
            var current = await CurrentAsync(caller.Principal);
            if (current == null) return ScreenResult<T>.SignIn();

            if (!await Confirmation.IsConfirmedAsync(current, password)) return back(null, NotConfirmed);
        }

        string message;
        try
        {
            message = await change();
        }
        catch (AccessModelException e)
        {
            return back(null, e.Message);
        }

        return back(message, null);
    }

    /// <summary>What a set of patterns selects, for the pattern editors.</summary>
    protected Task<PatternPreview> PreviewAsync(string[] repositoryPatterns, string[] includePatterns, string[] excludePatterns, string? testPackage, CancellationToken cancellationToken)
        => AccessPatternSupport.PreviewAsync(Repositories, repositoryPatterns, includePatterns, excludePatterns, testPackage, cancellationToken);
}
