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
using BaGet.Identity.Application.Models.Account;
using BaGet.Identity.Application.Models.Repositories;
using BaGet.Identity.Application.Models.Users;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Identity.Application.UseCases.Admin;

/// <summary>
/// One account as an administrator sees it: the details, its API keys (revocable), its organizations and a link to its packages,
/// and the access model's view of it (groups, project roles, effective permissions per repository). Revoking a key, approving the
/// account and changing group membership ask for the administrator's password and are refused with 503 while the
/// <see cref="MaintenanceGate"/> is closed.
/// </summary>
public sealed class AdminUserDetailUseCase : AdminUseCaseBase
{
    private static readonly ScreenRef Screen = ScreenCatalog.AdminUserDetail;

    private readonly IContext _context;
    private readonly ApiKeyService _apiKeys;
    private readonly PackageOwnershipService _ownership;
    private readonly AccountApprovalService _approvals;
    private readonly IAccessAdministration _access;
    private readonly IAccessEvaluator _evaluator;
    private readonly IRepositoryService _repositories;
    private readonly ILogger<AdminUserDetailUseCase> _logger;

    /// <summary>Creates the use case.</summary>
    public AdminUserDetailUseCase(
        UserManager<BaGetUser> users,
        PasswordConfirmation confirmation,
        IContext context,
        ApiKeyService apiKeys,
        PackageOwnershipService ownership,
        AccountApprovalService approvals,
        IAccessAdministration access,
        IAccessEvaluator evaluator,
        IRepositoryService repositories,
        ILogger<AdminUserDetailUseCase> logger,
        MaintenanceGate? gate = null)
        : base(users, confirmation, gate)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _apiKeys = apiKeys ?? throw new ArgumentNullException(nameof(apiKeys));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _approvals = approvals ?? throw new ArgumentNullException(nameof(approvals));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Shows the account; not found for an unknown user. <paramref name="package"/> is the package ID to evaluate the permissions for.</summary>
    public async Task<ScreenResult<UserDetailViewModel>> GetAsync(ClaimsPrincipal caller, string? name, string? package, CancellationToken cancellationToken)
    {
        var model = new UserDetailViewModel { Name = name, Package = package };

        return await LoadAsync(model, caller, cancellationToken)
            ? ScreenResult<UserDetailViewModel>.Ok(model)
            : ScreenResult<UserDetailViewModel>.NotFound();
    }

    /// <summary>Enables the account when it waits for approval, like the Users screen's Approve (password, gate).</summary>
    public async Task<ScreenResult<UserDetailViewModel>> ApproveAsync(ClaimsPrincipal caller, string? name, string? password, CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        var user = await FindAsync(name);
        if (user == null) return ScreenResult<UserDetailViewModel>.NotFound();

        var current = await CurrentAsync(caller);
        if (current == null) return ScreenResult<UserDetailViewModel>.SignIn();

        string? message = null, error = null;
        if (!await Confirmation.IsConfirmedAsync(current, password))
        {
            error = NotConfirmed;
        }
        else
        {
            switch (await _approvals.ApproveAsync(user.UserName, current.UserName, cancellationToken))
            {
                case AccountApprovalResult.Approved:
                    message = $"{user.UserName} was approved and can sign in.";
                    break;
                case AccountApprovalResult.NotPending:
                    message = $"{user.UserName} was not waiting for approval.";
                    break;
                default:
                    error = $"{user.UserName} was changed or removed meanwhile, so nothing was approved.";
                    break;
            }
        }

        return Back(user, message, error);
    }

    /// <summary>Revokes one of the account's API keys.</summary>
    public async Task<ScreenResult<UserDetailViewModel>> RevokeKeyAsync(ClaimsPrincipal caller, string? name, int key, string? password, CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        var user = await FindAsync(name);
        if (user == null) return ScreenResult<UserDetailViewModel>.NotFound();

        var current = await CurrentAsync(caller);
        if (current == null) return ScreenResult<UserDetailViewModel>.SignIn();

        string? message = null, error = null;
        if (!await Confirmation.IsConfirmedAsync(current, password))
        {
            error = NotConfirmed;
        }
        else if (await _apiKeys.RevokeAsync(user.Id, key, cancellationToken))
        {
            _logger.LogWarning("{Admin} revoked API key {Key} of {User}", current.UserName, key, user.UserName);
            message = "The API key was revoked.";
        }
        else
        {
            error = $"{user.UserName} has no such API key.";
        }

        return Back(user, message, error);
    }

    /// <summary>Adds the account to a group.</summary>
    public Task<ScreenResult<UserDetailViewModel>> AddGroupAsync(ClaimsPrincipal caller, string? name, string? group, string? password, CancellationToken cancellationToken)
        => ChangeGroupAsync(caller, name, group, password, add: true, cancellationToken);

    /// <summary>Removes the account from a group.</summary>
    public Task<ScreenResult<UserDetailViewModel>> RemoveGroupAsync(ClaimsPrincipal caller, string? name, string? group, string? password, CancellationToken cancellationToken)
        => ChangeGroupAsync(caller, name, group, password, add: false, cancellationToken);

    private static ScreenResult<UserDetailViewModel> Back(BaGetUser user, string? message, string? error)
        => Back<UserDetailViewModel>(Screen, Route(("name", user.UserName)), message, error);

    private async Task<BaGetUser?> FindAsync(string? name)
        => string.IsNullOrEmpty(name) ? null : await Users.FindByNameAsync(name);

    private async Task<ScreenResult<UserDetailViewModel>> ChangeGroupAsync(
        ClaimsPrincipal caller,
        string? name,
        string? group,
        string? password,
        bool add,
        CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        var user = await FindAsync(name);
        if (user == null || _context is NullContext) return ScreenResult<UserDetailViewModel>.NotFound();

        var current = await CurrentAsync(caller);
        if (current == null) return ScreenResult<UserDetailViewModel>.SignIn();

        string? message = null, error = null;
        if (!await Confirmation.IsConfirmedAsync(current, password))
        {
            error = NotConfirmed;
        }
        else
        {
            try
            {
                if (add)
                {
                    await _access.AddGroupMemberAsync(group ?? string.Empty, user.Id, current.UserName, cancellationToken);
                    message = $"{user.UserName} was added to {group}.";
                }
                else
                {
                    await _access.RemoveGroupMemberAsync(group ?? string.Empty, user.Id, current.UserName, cancellationToken);
                    message = $"{user.UserName} was removed from {group}.";
                }
            }
            catch (AccessModelException e)
            {
                error = e.Message;
            }
        }

        return Back(user, message, error);
    }

    private async Task<bool> LoadAsync(UserDetailViewModel model, ClaimsPrincipal caller, CancellationToken cancellationToken)
    {
        var account = await FindAsync(model.Name);
        if (account == null) return false;

        model.Account = new UserAccountInfo(account.UserName, account.Email, account.Created, account.IsAdministrator, account.PendingApproval);

        await LoadAccessAsync(model, account, cancellationToken);

        var keys = await _apiKeys.ListAsync(account.Id, cancellationToken);
        var repositories = await _apiKeys.RepositoryNamesAsync(keys, cancellationToken);
        model.Keys = keys
            .Select(key => new UserKeyRow(
                key.Key,
                key.Description,
                key.Scopes ?? Array.Empty<string>(),
                key.PackageGlobPattern,
                key.Created,
                key.Expires,
                ApiKeyService.DescribeRepositories(repositories.GetValueOrDefault(key.Key))))
            .ToList();
        model.Organizations = await _ownership.GetOrganizationsAsync(account.Id, cancellationToken) ?? (IReadOnlyList<Organization>)Array.Empty<Organization>();
        model.OwnedPackageIds = await _ownership.CountOwnedPackageIdsAsync(account.UserName, cancellationToken);

        await Confirmation.LoadAsync(model, caller);

        return true;
    }

    // The access model's view of the account: groups, project roles and the effective permissions per repository.
    private async Task LoadAccessAsync(UserDetailViewModel model, BaGetUser account, CancellationToken cancellationToken)
    {
        model.ModelAvailable = _context is not NullContext;
        if (!model.ModelAvailable) return;

        var package = string.IsNullOrWhiteSpace(model.Package) ? null : model.Package.Trim();

        model.Groups = await _access.GroupsOfUserAsync(account.Id, cancellationToken);
        model.ProjectRoles = await _access.ProjectRolesOfUserAsync(account.Id, cancellationToken);

        // What the account would be allowed through a session: its administrator flag is the only override.
        var principal = new BaGetPrincipal(account.Id, account.UserName, isSuperuser: false, ApiKeyScopes.All, "*", account.IsAdministrator);
        var rows = new List<RepositoryAccessRow>();
        foreach (var repository in await _repositories.ListAsync(cancellationToken))
        {
            rows.Add(new RepositoryAccessRow(repository, await _evaluator.ExplainAsync(principal, repository, package, cancellationToken)));
        }

        model.Access = rows
            .OrderBy(r => r.Repository.IsDefault ? 0 : 1)
            .ThenBy(r => r.Repository.Name, StringComparer.Ordinal)
            .ToList();
    }
}
