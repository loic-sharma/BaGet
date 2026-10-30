using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using BaGet.Identity.Application.Models.Users;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Identity.Application.UseCases.Account;

namespace BaGet.Identity.Application.UseCases.Admin;

/// <summary>
/// An administrator deleting another account: what goes, what happens to the organizations and to the package IDs only that user
/// owned (leave them without owners, or delete them with all their versions), then a password confirmation. Never the
/// administrator's own account, and never the last administrator.
/// </summary>
public sealed class AdminUserDeletionUseCase : AdminUseCaseBase
{
    private static readonly ScreenRef UsersScreen = ScreenCatalog.AdminUsers;

    private readonly AccountDeletionService _deletion;
    private readonly IOptionsSnapshot<BaGetOptions> _options;

    /// <summary>Creates the use case.</summary>
    public AdminUserDeletionUseCase(
        UserManager<BaGetUser> users,
        PasswordConfirmation confirmation,
        AccountDeletionService deletion,
        IOptionsSnapshot<BaGetOptions> options,
        MaintenanceGate? gate = null)
        : base(users, confirmation, gate)
    {
        _deletion = deletion ?? throw new ArgumentNullException(nameof(deletion));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    private bool IsReadOnlyMode => _options.Value.IsReadOnlyMode;

    /// <summary>Shows what the deletion does.</summary>
    public async Task<ScreenResult<DeleteUserViewModel>> GetAsync(ClaimsPrincipal caller, string? name, CancellationToken cancellationToken)
    {
        var (user, refusal) = await FindTargetAsync(caller, name);
        if (refusal != null) return refusal;

        var model = new DeleteUserViewModel { Name = name };
        await LoadAsync(model, user!, caller, cancellationToken);

        return ScreenResult<DeleteUserViewModel>.Ok(model);
    }

    /// <summary>
    /// Deletes the user after the administrator's password, then returns to the users. <paramref name="soleOwned"/> is "keep" (default) or "delete":
    /// what to do with the IDs only this user owns. A refusal shows the screen again (<see cref="ScreenStatus.Invalid"/>).
    /// </summary>
    public async Task<ScreenResult<DeleteUserViewModel>> DeleteAsync(
        ClaimsPrincipal caller,
        string? name,
        string? soleOwned,
        string? password,
        CancellationToken cancellationToken)
    {
        ThrowIfClosed();
        var (user, refusal) = await FindTargetAsync(caller, name);
        if (refusal != null) return refusal;

        var model = new DeleteUserViewModel { Name = name };
        if (soleOwned != null) model.SoleOwned = soleOwned;

        var current = await CurrentAsync(caller);
        var deletePackages = string.Equals(model.SoleOwned, "delete", StringComparison.OrdinalIgnoreCase);

        // The same refusal as the package page's hard delete: read-only mode freezes package removal.
        if (deletePackages && IsReadOnlyMode)
        {
            return await ShowAsync(model, DeleteUserViewModel.ReadOnlyMessage, user!, caller, cancellationToken);
        }

        if (!await Confirmation.IsConfirmedAsync(current, password))
        {
            return await ShowAsync(model, "The confirmation is incorrect, so the account was not deleted.", user!, caller, cancellationToken);
        }

        var userName = user!.UserName;
        var outcome = await _deletion.DeleteByAdministratorAsync(user, deletePackages, current?.UserName, cancellationToken);

        if (!outcome.Result.Succeeded)
        {
            return await ShowAsync(model, AccountSupportText.Describe(outcome.Result), user, caller, cancellationToken);
        }

        string? error = null;
        if (outcome.Failures.Count > 0)
        {
            error = "Could not delete: " + string.Join(", ", outcome.Failures) + ". They have no owners and are listed under Packages without owners.";
        }

        return Back<DeleteUserViewModel>(UsersScreen, null, Summarize(userName, outcome), error);
    }

    private async Task<ScreenResult<DeleteUserViewModel>> ShowAsync(DeleteUserViewModel model, string error, BaGetUser user, ClaimsPrincipal caller, CancellationToken cancellationToken)
    {
        model.Error = error;
        await LoadAsync(model, user, caller, cancellationToken);

        return ScreenResult<DeleteUserViewModel>.Invalid(model, error);
    }

    private static string Summarize(string? name, AdminDeletionResult outcome)
    {
        var text = $"{name} was deleted.";
        if (outcome.DeletedPackageIds.Count > 0) text += $" Packages deleted: {string.Join(", ", outcome.DeletedPackageIds)}.";
        if (outcome.KeptWithoutOwners.Count > 0) text += $" Packages left without owners: {string.Join(", ", outcome.KeptWithoutOwners)}.";

        return text;
    }

    // The user, or what to answer instead: not found for no such user, back to the list for oneself.
    private async Task<(BaGetUser? User, ScreenResult<DeleteUserViewModel>? Refusal)> FindTargetAsync(ClaimsPrincipal caller, string? name)
    {
        var current = await CurrentAsync(caller);
        if (current == null) return (null, ScreenResult<DeleteUserViewModel>.SignIn());

        var user = string.IsNullOrEmpty(name) ? null : await Users.FindByNameAsync(name);
        if (user == null) return (null, ScreenResult<DeleteUserViewModel>.NotFound());

        if (user.Id == current.Id)
        {
            return (null, Back<DeleteUserViewModel>(UsersScreen, null, null, "Delete your own account from your account settings."));
        }

        return (user, null);
    }

    private async Task LoadAsync(DeleteUserViewModel model, BaGetUser user, ClaimsPrincipal caller, CancellationToken cancellationToken)
    {
        model.Preview = await _deletion.PreviewAdministratorDeletionAsync(user, cancellationToken);
        model.LastAdministrator = (await _deletion.GetBlockersAsync(user, cancellationToken)).LastAdministrator;
        model.IsReadOnlyMode = IsReadOnlyMode;

        await Confirmation.LoadAsync(model, caller);
    }
}
