using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using BaGet.Identity.Application.Models.Account;
using BaGet.Identity.Application.Models.Organizations;
using BaGet.Identity.Application.Models.Packages;
using BaGet.Identity.Application.Models.Repositories;
using BaGet.Identity.Application.Services.Ownership;
using BaGet.Identity.Application.UseCases.Admin;

namespace BaGet.Identity.Application.UseCases.Account;

/// <summary>
/// The signed-in user's own overview screens: their organizations (create one, manage the members of those they administer), "My packages" and
/// "My access", and the redirect from "my profile" to the profile page. Every method needs a signed-in session; none changes credentials.
/// </summary>
public sealed class AccountOverviewUseCase
{
    private readonly UserManager<BaGetUser> _users;
    private readonly PackageOwnershipService _ownership;
    private readonly IServiceProvider _services;

    /// <summary>Creates the use case.</summary>
    public AccountOverviewUseCase(UserManager<BaGetUser> users, PackageOwnershipService ownership, IServiceProvider services)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    // ----- organizations

    /// <summary>The organizations of the user.</summary>
    public async Task<ScreenResult<OrganizationsViewModel>> ShowOrganizationsAsync(ClaimsPrincipal caller, CancellationToken cancellationToken)
    {
        var model = new OrganizationsViewModel();
        await LoadOrganizationsAsync(caller, model, cancellationToken);

        return ScreenResult<OrganizationsViewModel>.Ok(model);
    }

    /// <summary>Create an organization.</summary>
    public async Task<ScreenResult<OrganizationsViewModel>> CreateOrganizationAsync(ClaimsPrincipal caller, CreateOrganizationForm? form, CancellationToken cancellationToken)
    {
        var name = form?.Name;
        var created = await _ownership.CreateOrganizationAsync(name!, _users.GetUserId(caller), cancellationToken);

        return await DoneAsync(caller, created ? $"Created {name}." : "That name is taken.", cancellationToken);
    }

    /// <summary>Add a member (organization administrators only).</summary>
    public async Task<ScreenResult<OrganizationsViewModel>> AddMemberAsync(ClaimsPrincipal caller, AddOrganizationMemberForm? form, CancellationToken cancellationToken)
    {
        var organization = form?.Organization;
        if (!await _ownership.IsOrganizationAdminAsync(organization, _users.GetUserId(caller), cancellationToken))
        {
            return ScreenResult<OrganizationsViewModel>.Forbidden();
        }

        var userName = form!.UserName;
        var user = string.IsNullOrWhiteSpace(userName) ? null : await _users.FindByNameAsync(userName.Trim());
        if (user == null)
        {
            return await DoneAsync(caller, "There is no user with that name.", cancellationToken);
        }

        var added = await _ownership.AddMemberAsync(organization!, user.Id, form.IsAdmin, cancellationToken);

        return await DoneAsync(caller, added ? $"{user.UserName} joined {organization}." : $"{user.UserName} is already a member.", cancellationToken);
    }

    /// <summary>Remove a member (an administrator removes anyone; any member may leave).</summary>
    public async Task<ScreenResult<OrganizationsViewModel>> RemoveMemberAsync(ClaimsPrincipal caller, RemoveOrganizationMemberForm? form, CancellationToken cancellationToken)
    {
        var organization = form?.Organization;
        var currentUserId = _users.GetUserId(caller);
        var user = await _users.FindByNameAsync(form?.UserName ?? string.Empty);

        // An administrator may remove anyone; any member may leave.
        if (user == null
            || (user.Id != currentUserId && !await _ownership.IsOrganizationAdminAsync(organization, currentUserId, cancellationToken)))
        {
            return ScreenResult<OrganizationsViewModel>.Forbidden();
        }

        var removed = await _ownership.RemoveMemberAsync(organization!, user.Id, cancellationToken);

        return await DoneAsync(
            caller,
            removed ? $"{user.UserName} left {organization}." : "An organization must keep at least one administrator.",
            cancellationToken);
    }

    private async Task<ScreenResult<OrganizationsViewModel>> DoneAsync(ClaimsPrincipal caller, string message, CancellationToken cancellationToken)
    {
        var model = new OrganizationsViewModel { Message = message };
        await LoadOrganizationsAsync(caller, model, cancellationToken);

        return ScreenResult<OrganizationsViewModel>.Ok(model);
    }

    private async Task LoadOrganizationsAsync(ClaimsPrincipal caller, OrganizationsViewModel model, CancellationToken cancellationToken)
    {
        var userId = _users.GetUserId(caller);
        var views = new List<AccountOrganization>();

        #pragma warning disable CS8602
        foreach (var organization in await _ownership.GetOrganizationsAsync(userId, cancellationToken))
        #pragma warning restore CS8602
        {
            var members = await _ownership.GetMembersAsync(organization.Name, cancellationToken);
            var names = new List<AccountOrganizationMember>();
            foreach (var member in members)
            {
                #pragma warning disable CS8604
                var user = await _users.FindByIdAsync(member.UserId);
                #pragma warning restore CS8604
                if (user?.UserName != null) names.Add(new AccountOrganizationMember(user.UserName, member.IsAdmin));
            }

            views.Add(new AccountOrganization
            {
                Name = organization.Name,
                IsAdmin = members.Any(m => m.UserId == userId && m.IsAdmin),
                Members = names.OrderBy(n => n.UserName, StringComparer.OrdinalIgnoreCase).ToList(),
            });
        }

        model.Organizations = views;
    }

    // ----- my packages

    /// <summary>
    /// The package IDs the signed-in user owns, directly or through an organization, grouped by the repositories the user may read (ownership is
    /// per repository); a repository where the user owns nothing is not listed.
    /// </summary>
    public async Task<ScreenResult<AccountPackagesViewModel>> ShowPackagesAsync(ClaimsPrincipal caller, CancellationToken cancellationToken)
    {
        var access = _services.GetRequiredService<IRepositoryAccess>();
        var scopes = _services.GetRequiredService<IServiceScopeFactory>();
        var userId = _users.GetUserId(caller);
        var groups = new List<RepositoryPackages>();

        foreach (var repository in await access.ReadableAsync(caller, cancellationToken))
        {
            // A scope pinned to the repository: ownership and packages are repository-scoped tables.
            using var scope = scopes.CreateRepositoryScope(repository);

            // Ownership, not the package table, decides what is listed: an ID whose every
            // version was deleted is still the user's, and hiding it would hide the page
            // where its owners are managed.
            var ids = await scope.ServiceProvider.GetRequiredService<PackageOwnershipService>().GetOwnedPackageIdsAsync(
                userId,
                caller.Identity?.Name,
                cancellationToken);

            if (ids == null || ids.Count == 0) continue;

            if (repository.Format == RepositoryFormat.Npm)
            {
                groups.Add(new RepositoryPackages(repository, await PackagesModel.SummariseNpmAsync(scope.ServiceProvider.GetRequiredService<INpmRegistry>(), ids, cancellationToken)));
                continue;
            }

            if (repository.Format == RepositoryFormat.Maven)
            {
                groups.Add(new RepositoryPackages(repository, await PackagesModel.SummariseMavenAsync(scope.ServiceProvider.GetRequiredService<IMavenRepository>(), ids, cancellationToken)));
                continue;
            }

            groups.Add(new RepositoryPackages(
                repository,
                PackagesModel.Summarise(ids, await PackagesModel.ListVersionsAsync(
                    scope.ServiceProvider.GetRequiredService<IContext>(), ids, cancellationToken))));
        }

        return ScreenResult<AccountPackagesViewModel>.Ok(new AccountPackagesViewModel { Groups = groups });
    }

    // ----- my access

    /// <summary>
    /// The signed-in user's own effective permissions: for each repository where they hold any action, which of Read, Annotate, Deploy, Delete and
    /// Manage, and the permission targets or project roles that grant them (optionally for one package ID, to see how include/exclude patterns
    /// apply). Repositories where the user holds nothing are not listed, so the screen discloses nothing about private repositories.
    /// </summary>
    public async Task<ScreenResult<AccountAccessViewModel>> ShowAccessAsync(ClaimsPrincipal caller, string? package, CancellationToken cancellationToken)
    {
        var principal = await _services.GetRequiredService<ISessionPrincipalFactory>().CreateAsync(caller);
        if (principal == null) return ScreenResult<AccountAccessViewModel>.SignIn();

        var model = new AccountAccessViewModel { Package = package };
        model.ModelAvailable = _services.GetRequiredService<IContext>() is not NullContext;
        var evaluator = _services.GetRequiredService<IAccessEvaluator>();
        var packageId = string.IsNullOrWhiteSpace(model.Package) ? null : model.Package.Trim();

        var rows = new List<RepositoryAccessRow>();
        foreach (var repository in await _services.GetRequiredService<IRepositoryService>().ListAsync(cancellationToken))
        {
            var explanation = await evaluator.ExplainAsync(principal, repository, packageId, cancellationToken);
            if (explanation.Actions != AccessAction.None) rows.Add(new RepositoryAccessRow(repository, explanation));
        }

        model.Rows = rows
            .OrderBy(r => r.Repository.IsDefault ? 0 : 1)
            .ThenBy(r => r.Repository.Name, StringComparer.Ordinal)
            .ToList();

        if (model.ModelAvailable && principal.UserId != null)
        {
            model.AdministeredProjects = (await _services.GetRequiredService<IAccessAdministration>().ProjectRolesOfUserAsync(principal.UserId, cancellationToken))
                .Where(r => r.Role == ProjectRole.Admin)
                .Select(r => r.Project)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        return ScreenResult<AccountAccessViewModel>.Ok(model);
    }

    // ----- my profile

    /// <summary>The signed-in user's own profile: continues at <c>/profiles/{name}</c>.</summary>
    public ScreenResult<EmptyScreen> ShowMyProfile(ClaimsPrincipal caller)
    {
        #pragma warning disable CS8604
        return ScreenResult<EmptyScreen>.GoTo(ScreenNext.Local($"~/profiles/{Uri.EscapeDataString(caller.Identity?.Name)}"));
        #pragma warning restore CS8604
    }
}
