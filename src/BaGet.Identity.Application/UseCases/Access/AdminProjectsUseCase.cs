using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Identity;
using BaGet.Identity.Application.Models.Access;
using BaGet.Identity.Application.Models.Shared;

namespace BaGet.Identity.Application.UseCases.Access;

/// <summary>
/// The project screens: the list with the new-project form, and one project (its repositories, members with their roles, and the permission targets that
/// apply only to its repositories). Server administrators see every project and create and delete them and assign repositories; a project's own
/// administrators (the project is the <c>name</c> route value, checked by the gate with <see cref="AccessAuthorization"/>) see the projects they
/// administer and manage members and targets. Member changes and deletions ask for the password again; every change is refused with 503 while the
/// <see cref="MaintenanceGate"/> is closed and goes through <see cref="IAccessAdministration"/>, which audits it.
/// </summary>
public sealed class AdminProjectsUseCase : AccessUseCaseBase
{
    private static readonly ScreenRef ListScreen = ScreenCatalog.AdminProjects;
    private static readonly ScreenRef DetailScreen = ScreenCatalog.AdminProjectDetail;

    /// <summary>Creates the use case.</summary>
    public AdminProjectsUseCase(
        UserManager<BaGetUser> users,
        PasswordConfirmation confirmation,
        IAccessAdministration access,
        IRepositoryService repositories,
        MaintenanceGate? gate = null)
        : base(users, confirmation, access, repositories, gate)
    {
    }

    /// <summary>Lists the projects the caller may open.</summary>
    public async Task<ScreenResult<ProjectsViewModel>> ListAsync(AccessCaller caller, CancellationToken cancellationToken)
        => ScreenResult<ProjectsViewModel>.Ok(await LoadListAsync(new ProjectsViewModel(), caller, cancellationToken));

    /// <summary>Creates a project (administrators only) and opens it; a refusal shows the list with the reason.</summary>
    public async Task<ScreenResult<ProjectsViewModel>> CreateAsync(AccessCaller caller, ProjectCreation form, CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        if (!caller.IsServerAdministrator) return ScreenResult<ProjectsViewModel>.NotFound();

        var model = new ProjectsViewModel { Name = form.Name, DisplayName = form.DisplayName };
        try
        {
            var project = await Access.CreateProjectAsync(
                form.Name ?? string.Empty,
                string.IsNullOrWhiteSpace(form.DisplayName) ? null : form.DisplayName.Trim(),
                Actor(caller),
                cancellationToken);

            return Back<ProjectsViewModel>(DetailScreen, Route(("name", project.Name)), $"Project {project.Name} was created.", null);
        }
        catch (AccessModelException e)
        {
            model.Error = e.Message;

            return ScreenResult<ProjectsViewModel>.Ok(await LoadListAsync(model, caller, cancellationToken));
        }
    }

    /// <summary>Shows the project; not found when there is none.</summary>
    public async Task<ScreenResult<ProjectDetailViewModel>> GetAsync(AccessCaller caller, string? name, CancellationToken cancellationToken)
    {
        var model = new ProjectDetailViewModel { Name = name };

        return await LoadAsync(model, caller, cancellationToken)
            ? ScreenResult<ProjectDetailViewModel>.Ok(model)
            : ScreenResult<ProjectDetailViewModel>.NotFound();
    }

    /// <summary>Gives a user or group a role in the project, replacing an earlier one.</summary>
    public Task<ScreenResult<ProjectDetailViewModel>> SetMemberAsync(AccessCaller caller, string? name, ProjectMemberForm form, CancellationToken cancellationToken)
        => ActAsync<ProjectDetailViewModel>(caller, form.Password, confirm: true, async () =>
        {
            if (form.Type == PrincipalType.Organization) throw new AccessModelException("A project member is a user or a group.");

            var id = await Access.ResolvePrincipalAsync(form.Type, form.PrincipalName ?? string.Empty, cancellationToken)
                     ?? throw new AccessModelException($"There is no {form.Type.ToString().ToLowerInvariant()} named '{form.PrincipalName}'.");
            await Access.SetProjectMemberAsync(name ?? string.Empty, form.Type, id, form.Role, Actor(caller), cancellationToken);

            return $"{form.PrincipalName} is now {form.Role} of {name}.";
        }, (message, error) => BackToProject(name, message, error));

    /// <summary>Takes a user or group out of the project.</summary>
    public Task<ScreenResult<ProjectDetailViewModel>> RemoveMemberAsync(AccessCaller caller, string? name, PrincipalRemoval form, CancellationToken cancellationToken)
        => ActAsync<ProjectDetailViewModel>(caller, form.Password, confirm: true, async () =>
        {
            await Access.RemoveProjectMemberAsync(name ?? string.Empty, form.Type, form.PrincipalId ?? string.Empty, Actor(caller), cancellationToken);

            return "The member was removed.";
        }, (message, error) => BackToProject(name, message, error));

    /// <summary>Puts a repository in the project (server administrators only); it leaves any project it was in.</summary>
    public Task<ScreenResult<ProjectDetailViewModel>> AssignRepositoryAsync(AccessCaller caller, string? name, ProjectRepositoryForm form, CancellationToken cancellationToken)
        => ActAsync<ProjectDetailViewModel>(caller, form.Password, confirm: true, async () =>
        {
            if (!caller.IsServerAdministrator) throw new AccessModelException("Only server administrators assign repositories to projects.");

            await Access.SetRepositoryProjectAsync(form.Repository ?? string.Empty, name, Actor(caller), cancellationToken);

            return $"Repository {form.Repository} is now in {name}.";
        }, (message, error) => BackToProject(name, message, error));

    /// <summary>Takes a repository out of the project (server administrators only).</summary>
    public Task<ScreenResult<ProjectDetailViewModel>> UnassignRepositoryAsync(AccessCaller caller, string? name, ProjectRepositoryForm form, CancellationToken cancellationToken)
        => ActAsync<ProjectDetailViewModel>(caller, form.Password, confirm: true, async () =>
        {
            if (!caller.IsServerAdministrator) throw new AccessModelException("Only server administrators take repositories out of projects.");

            await Access.SetRepositoryProjectAsync(form.Repository ?? string.Empty, null, Actor(caller), cancellationToken);

            return $"Repository {form.Repository} is no longer in {name}.";
        }, (message, error) => BackToProject(name, message, error));

    /// <summary>Creates a permission target that applies only to the project's repositories and opens it; a refusal shows the project with the reason.</summary>
    public async Task<ScreenResult<ProjectDetailViewModel>> CreateTargetAsync(AccessCaller caller, string? name, ProjectTargetForm form, CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        var model = DetailModel(name, form);
        if (!await LoadAsync(model, caller, cancellationToken)) return ScreenResult<ProjectDetailViewModel>.NotFound();

        try
        {
            var target = await Access.CreateTargetAsync(
                form.TargetName ?? string.Empty,
                AccessPatterns.Split(form.RepositoryPatterns),
                AccessPatterns.Split(form.IncludePatterns),
                AccessPatterns.Split(form.ExcludePatterns),
                model.Project!.Project.Key,
                Actor(caller),
                cancellationToken);

            return Back<ProjectDetailViewModel>(ScreenCatalog.AdminPermissionDetail, Route(("name", target.Name)), $"Permission target {target.Name} was created. Add grants to it.", null);
        }
        catch (AccessModelException e)
        {
            model.Error = e.Message;

            return ScreenResult<ProjectDetailViewModel>.Ok(model);
        }
    }

    /// <summary>Previews the new target's patterns without saving anything.</summary>
    public async Task<ScreenResult<ProjectDetailViewModel>> PreviewAsync(AccessCaller caller, string? name, ProjectTargetForm form, CancellationToken cancellationToken)
    {
        var model = DetailModel(name, form);
        if (!await LoadAsync(model, caller, cancellationToken)) return ScreenResult<ProjectDetailViewModel>.NotFound();

        // Only the project's own repositories can be reached, whatever the patterns say.
        var preview = await PreviewAsync(
            AccessPatterns.Split(form.RepositoryPatterns), AccessPatterns.Split(form.IncludePatterns), AccessPatterns.Split(form.ExcludePatterns), form.TestPackage, cancellationToken);
        model.Preview = preview with { Repositories = preview.Repositories.Where(r => model.Project!.Repositories.Contains(r, StringComparer.Ordinal)).ToList() };

        return ScreenResult<ProjectDetailViewModel>.Ok(model);
    }

    /// <summary>Deletes the project (server administrators only) with its members and targets; the repositories stay. On success the list is shown.</summary>
    public Task<ScreenResult<ProjectDetailViewModel>> DeleteAsync(AccessCaller caller, string? name, PasswordForm form, CancellationToken cancellationToken)
    {
        var deleted = false;

        return ActAsync<ProjectDetailViewModel>(caller, form.Password, confirm: true, async () =>
        {
            if (!caller.IsServerAdministrator) throw new AccessModelException("Only server administrators delete projects.");

            await Access.DeleteProjectAsync(name ?? string.Empty, Actor(caller), cancellationToken);
            deleted = true;

            return $"Project {name} was deleted.";
        }, (message, error) => deleted
            ? Back<ProjectDetailViewModel>(ListScreen, null, message, error)
            : BackToProject(name, message, error));
    }

    private static ScreenResult<ProjectDetailViewModel> BackToProject(string? name, string? message, string? error)
        => Back<ProjectDetailViewModel>(DetailScreen, Route(("name", name)), message, error);

    private static ProjectDetailViewModel DetailModel(string? name, ProjectTargetForm form)
        => new()
        {
            Name = name,
            TargetName = form.TargetName,
            RepositoryPatterns = form.RepositoryPatterns,
            IncludePatterns = form.IncludePatterns,
            ExcludePatterns = form.ExcludePatterns,
            TestPackage = form.TestPackage,
        };

    private async Task<ProjectsViewModel> LoadListAsync(ProjectsViewModel model, AccessCaller caller, CancellationToken cancellationToken)
    {
        model.IsServerAdministrator = caller.IsServerAdministrator;
        var all = await Access.ListProjectsAsync(cancellationToken);
        if (caller.IsServerAdministrator)
        {
            model.Projects = all;
            return model;
        }

        var mine = new HashSet<string>(await AccessAuthorization.AdministeredAsync(Access, UserIdOf(caller), cancellationToken), StringComparer.Ordinal);
        model.Projects = all.Where(p => mine.Contains(p.Name)).ToList();

        return model;
    }

    private async Task<bool> LoadAsync(ProjectDetailViewModel model, AccessCaller caller, CancellationToken cancellationToken)
    {
        model.IsServerAdministrator = caller.IsServerAdministrator;
        await LoadConfirmationAsync(model, caller);

        model.Project = await Access.GetProjectAsync(model.Name ?? string.Empty, cancellationToken);
        if (model.Project == null) return false;

        // Only a server administrator assigns repositories; the view shows the list to nobody else, so the model must not carry it.
        if (caller.IsServerAdministrator)
        {
            var all = await Repositories.ListAsync(cancellationToken);
            model.Assignable = all
                .Where(r => !model.Project.Repositories.Contains(r.Name, StringComparer.Ordinal))
                .Select(r => r.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
        }

        return true;
    }
}
