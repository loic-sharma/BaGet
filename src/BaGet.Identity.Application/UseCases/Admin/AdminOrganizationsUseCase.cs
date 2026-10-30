using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using BaGet.Identity.Application.Models.Organizations;
using BaGet.Identity.Application.Services.Organizations;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Identity.Application.UseCases.Admin;

/// <summary>
/// The organization screens of the administrator: the list, and one organization: rename it (package ownership follows in every
/// repository, atomically), add, promote and remove members (an organization keeps at least one administrator) and delete it
/// (refused while it owns packages). Every action asks for the administrator's password again and is refused with 503 while the
/// <see cref="MaintenanceGate"/> is closed.
/// </summary>
public sealed class AdminOrganizationsUseCase : AdminUseCaseBase
{
    private static readonly ScreenRef DetailScreen = ScreenCatalog.AdminOrganizationDetail;
    private static readonly ScreenRef ListScreen = ScreenCatalog.AdminOrganizations;

    private readonly PackageOwnershipService _ownership;
    private readonly ILogger<AdminOrganizationsUseCase> _logger;

    /// <summary>Creates the use case.</summary>
    public AdminOrganizationsUseCase(
        UserManager<BaGetUser> users,
        PasswordConfirmation confirmation,
        PackageOwnershipService ownership,
        ILogger<AdminOrganizationsUseCase> logger,
        MaintenanceGate? gate = null)
        : base(users, confirmation, gate)
    {
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Lists the organizations.</summary>
    public async Task<ScreenResult<AdminOrganizationsViewModel>> ListAsync(CancellationToken cancellationToken)
        => ScreenResult<AdminOrganizationsViewModel>.Ok(new AdminOrganizationsViewModel
        {
            Organizations = await _ownership.GetAllOrganizationsAsync(cancellationToken),
        });

    /// <summary>Shows the organization; not found when there is none.</summary>
    public async Task<ScreenResult<OrganizationDetailViewModel>> GetAsync(ClaimsPrincipal caller, string? name, CancellationToken cancellationToken)
    {
        var model = new OrganizationDetailViewModel { Name = name };

        return await LoadAsync(model, caller, cancellationToken)
            ? ScreenResult<OrganizationDetailViewModel>.Ok(model)
            : ScreenResult<OrganizationDetailViewModel>.NotFound();
    }

    /// <summary>Renames the organization.</summary>
    public Task<ScreenResult<OrganizationDetailViewModel>> RenameAsync(ClaimsPrincipal caller, string? name, string? newName, string? password, CancellationToken cancellationToken)
        => ActAsync(caller, name, password, cancellationToken, async (current, outcome) =>
        {
            var result = await _ownership.RenameOrganizationAsync(name, newName, cancellationToken);
            if (result == OrganizationChange.Success)
            {
                Log(current, "renamed organization {Old} to {New}", name, newName?.Trim());
                outcome.Name = newName!.Trim();
                outcome.Message = $"The organization is now called {outcome.Name}.";
            }
            else
            {
                outcome.Error = Describe(result);
            }
        });

    /// <summary>Deletes the organization; on success the list is shown with the outcome.</summary>
    public async Task<ScreenResult<OrganizationDetailViewModel>> DeleteAsync(ClaimsPrincipal caller, string? name, string? password, CancellationToken cancellationToken)
    {
        var deleted = false;
        string? message = null;
        var result = await ActAsync(caller, name, password, cancellationToken, async (current, outcome) =>
        {
            var change = await _ownership.DeleteOrganizationAsync(name, cancellationToken, current.UserName);
            if (change == OrganizationChange.Success)
            {
                Log(current, "deleted organization {Old}", name, null);
                message = $"{name} was deleted."; // flashed to the list
                deleted = true;
            }
            else
            {
                outcome.Error = Describe(change);
            }
        });

        return deleted ? Back<OrganizationDetailViewModel>(ListScreen, null, message, null) : result;
    }

    /// <summary>Adds a user to the organization.</summary>
    public Task<ScreenResult<OrganizationDetailViewModel>> AddMemberAsync(ClaimsPrincipal caller, string? name, string? userName, bool isAdmin, string? password, CancellationToken cancellationToken)
        => ActAsync(caller, name, password, cancellationToken, async (current, outcome) =>
        {
            var user = string.IsNullOrWhiteSpace(userName) ? null : await Users.FindByNameAsync(userName.Trim());
            if (user == null)
            {
                outcome.Error = "There is no user with that name.";
            }
            else if (await _ownership.AddMemberAsync(name!, user.Id, isAdmin, cancellationToken))
            {
                Log(current, "added {New} to organization {Old}", name, user.UserName);
                outcome.Message = $"{user.UserName} joined {name}.";
            }
            else
            {
                outcome.Error = $"{user.UserName} is already a member.";
            }
        });

    /// <summary>Makes a member an administrator of the organization, or not.</summary>
    public Task<ScreenResult<OrganizationDetailViewModel>> SetAdminAsync(ClaimsPrincipal caller, string? name, string? userName, bool isAdmin, string? password, CancellationToken cancellationToken)
        => ActAsync(caller, name, password, cancellationToken, async (current, outcome) =>
        {
            var user = string.IsNullOrWhiteSpace(userName) ? null : await Users.FindByNameAsync(userName.Trim());
            var result = user == null
                ? OrganizationChange.NotFound
                : await _ownership.SetMemberAdminAsync(name!, user.Id, isAdmin, cancellationToken);
            if (result == OrganizationChange.Success)
            {
                Log(current, "set administrator={New} for a member of organization {Old}", name, isAdmin.ToString());
                outcome.Message = isAdmin ? $"{user!.UserName} now administers {name}." : $"{user!.UserName} no longer administers {name}.";
            }
            else
            {
                outcome.Error = Describe(result);
            }
        });

    /// <summary>Removes a member from the organization.</summary>
    public Task<ScreenResult<OrganizationDetailViewModel>> RemoveMemberAsync(ClaimsPrincipal caller, string? name, string? userName, string? password, CancellationToken cancellationToken)
        => ActAsync(caller, name, password, cancellationToken, async (current, outcome) =>
        {
            var user = string.IsNullOrWhiteSpace(userName) ? null : await Users.FindByNameAsync(userName.Trim());
            if (user == null)
            {
                outcome.Error = "There is no user with that name.";
            }
            else if (await _ownership.RemoveMemberAsync(name!, user.Id, cancellationToken))
            {
                Log(current, "removed {New} from organization {Old}", name, user.UserName);
                outcome.Message = $"{user.UserName} left {name}.";
            }
            else
            {
                outcome.Error = "An organization must keep at least one administrator, and the user must be a member.";
            }
        });

    private static string Describe(OrganizationChange change) => change switch
    {
        OrganizationChange.NameTaken => "That name is taken.",
        OrganizationChange.InvalidName => "That is not a valid name: it must not be blank or contain / \\ ? # %.",
        OrganizationChange.OwnsPackages => "The organization owns packages, so it cannot be deleted. Remove it as an owner of those packages first.",
        OrganizationChange.LastAdministrator => "An organization must keep at least one administrator.",
        _ => "There is no such organization or member.",
    };

    private void Log(BaGetUser admin, string template, string? oldValue, string? newValue)
        => _logger.LogWarning("{Admin} " + template, admin.UserName, oldValue, newValue);

    // Refuse during maintenance, confirm the password, run the change, then show the (possibly renamed) screen.
    private async Task<ScreenResult<OrganizationDetailViewModel>> ActAsync(
        ClaimsPrincipal caller,
        string? name,
        string? password,
        CancellationToken cancellationToken,
        Func<BaGetUser, OrganizationChangeOutcome, Task> change)
    {
        ThrowIfClosed();

        var current = await CurrentAsync(caller);
        if (current == null) return ScreenResult<OrganizationDetailViewModel>.SignIn();

        if (await _ownership.FindOrganizationAsync(name, cancellationToken) == null) return ScreenResult<OrganizationDetailViewModel>.NotFound();

        var outcome = new OrganizationChangeOutcome { Name = name };
        if (!await Confirmation.IsConfirmedAsync(current, password))
        {
            outcome.Error = NotConfirmed;
        }
        else
        {
            await change(current, outcome);
        }

        return Back<OrganizationDetailViewModel>(DetailScreen, Route(("name", outcome.Name)), outcome.Message, outcome.Error);
    }

    private async Task<bool> LoadAsync(OrganizationDetailViewModel model, ClaimsPrincipal caller, CancellationToken cancellationToken)
    {
        model.Organization = await _ownership.FindOrganizationAsync(model.Name, cancellationToken);
        if (model.Organization == null) return false;

        var rows = new List<OrganizationMemberRow>();
        foreach (var member in await _ownership.GetMembersAsync(model.Organization.Name, cancellationToken))
        {
            #pragma warning disable CS8604
            var user = await Users.FindByIdAsync(member.UserId);
            #pragma warning restore CS8604
            if (user != null) rows.Add(new OrganizationMemberRow(user.UserName, member.IsAdmin));
        }

        model.Members = rows.OrderBy(r => r.UserName, StringComparer.OrdinalIgnoreCase).ToList();
        model.OwnedPackageIds = await _ownership.CountOwnedPackageIdsAsync(model.Organization.Name, cancellationToken);

        await Confirmation.LoadForRequestAsync(model, caller);

        return true;
    }
}
