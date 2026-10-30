using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Identity;
using BaGet.Identity.Application.Models.Access;
using BaGet.Identity.Application.Models.Policies;
using BaGet.Identity.Application.Models.Shared;

namespace BaGet.Identity.Application.UseCases.Access;

/// <summary>
/// The permission target screens of the administrator: the list with the new-target form (server administrators only), and one target (also open to the
/// administrators of the target's project for a project target): its repository and package patterns (with a preview of the repositories they match and a
/// test box for package IDs), and the grants, which users, groups or organizations hold which of Read, Annotate, Deploy, Delete and Manage through it.
/// Saving patterns, setting or removing a grant and deleting ask for the password again; every change is refused with 503 while the
/// <see cref="MaintenanceGate"/> is closed and goes through <see cref="IAccessAdministration"/>, which audits it.
/// </summary>
public sealed class AdminPermissionsUseCase : AccessUseCaseBase
{
    private static readonly ScreenRef ListScreen = ScreenCatalog.AdminPermissions;
    private static readonly ScreenRef DetailScreen = ScreenCatalog.AdminPermissionDetail;

    /// <summary>Creates the use case.</summary>
    public AdminPermissionsUseCase(
        UserManager<BaGetUser> users,
        PasswordConfirmation confirmation,
        IAccessAdministration access,
        IRepositoryService repositories,
        MaintenanceGate? gate = null)
        : base(users, confirmation, access, repositories, gate)
    {
    }

    /// <summary>Lists the targets.</summary>
    public async Task<ScreenResult<PermissionsViewModel>> ListAsync(AccessCaller caller, CancellationToken cancellationToken)
        => ScreenResult<PermissionsViewModel>.Ok(await LoadListAsync(new PermissionsViewModel(), caller, cancellationToken));

    /// <summary>Creates a target and opens it; a refusal shows the list with the reason.</summary>
    public async Task<ScreenResult<PermissionsViewModel>> CreateAsync(AccessCaller caller, PermissionTargetForm form, CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        var model = ListModel(form);
        try
        {
            int? projectKey = null;
            if (!string.IsNullOrWhiteSpace(form.Project))
            {
                var project = await Access.GetProjectAsync(form.Project.Trim(), cancellationToken)
                              ?? throw new AccessModelException($"There is no project named '{form.Project}'.");
                projectKey = project.Project.Key;
            }

            var target = await Access.CreateTargetAsync(
                form.Name ?? string.Empty,
                AccessPatterns.Split(form.RepositoryPatterns),
                AccessPatterns.Split(form.IncludePatterns),
                AccessPatterns.Split(form.ExcludePatterns),
                projectKey,
                Actor(caller),
                cancellationToken);

            return Back<PermissionsViewModel>(DetailScreen, Route(("name", target.Name)), $"Permission target {target.Name} was created. Add grants to it.", null);
        }
        catch (AccessModelException e)
        {
            model.Error = e.Message;

            return ScreenResult<PermissionsViewModel>.Ok(await LoadListAsync(model, caller, cancellationToken));
        }
    }

    /// <summary>Previews the form patterns without saving anything.</summary>
    public async Task<ScreenResult<PermissionsViewModel>> PreviewAsync(AccessCaller caller, PermissionTargetForm form, CancellationToken cancellationToken)
    {
        var model = await LoadListAsync(ListModel(form), caller, cancellationToken);
        model.Preview = await PreviewAsync(
            AccessPatterns.Split(form.RepositoryPatterns), AccessPatterns.Split(form.IncludePatterns), AccessPatterns.Split(form.ExcludePatterns), form.TestPackage, cancellationToken);

        return ScreenResult<PermissionsViewModel>.Ok(model);
    }

    /// <summary>Re-creates the built-in groups and targets that are missing (the upgrade defaults).</summary>
    public Task<ScreenResult<PermissionsViewModel>> RestoreBuiltInsAsync(AccessCaller caller, PasswordForm form, CancellationToken cancellationToken)
        => ActAsync<PermissionsViewModel>(caller, form.Password, confirm: true, async () =>
        {
            await Access.RestoreBuiltInsAsync(Actor(caller), cancellationToken);

            return "The built-in groups and permission targets were restored where they were missing.";
        }, (message, error) => Back<PermissionsViewModel>(ListScreen, null, message, error));

    /// <summary>Shows the target (not found when there is none); <paramref name="principalType"/> and <paramref name="principalName"/> pre-fill the grant form.</summary>
    public async Task<ScreenResult<PermissionDetailViewModel>> GetAsync(
        AccessCaller caller, string? name, string? testPackage, PrincipalType? principalType, string? principalName, CancellationToken cancellationToken)
    {
        var model = DetailModel(name, new PermissionTargetForm(TestPackage: testPackage, PrincipalType: principalType, PrincipalName: principalName));
        if (!await LoadAsync(model, caller, cancellationToken)) return ScreenResult<PermissionDetailViewModel>.NotFound();

        var target = model.Target!;
        model.RepositoryPatterns = AccessPatternSupport.Lines(target.Target.RepositoryPatterns);
        model.IncludePatterns = AccessPatternSupport.Lines(target.Target.IncludePatterns);
        model.ExcludePatterns = AccessPatternSupport.Lines(target.Target.ExcludePatterns);
        model.Preview = await RestrictToProjectAsync(model, await PreviewAsync(
            target.Target.RepositoryPatterns.ToArray(), target.Target.IncludePatterns.ToArray(), target.Target.ExcludePatterns.ToArray(), model.TestPackage, cancellationToken), cancellationToken);

        if (!string.IsNullOrWhiteSpace(model.PrincipalName))
        {
            var id = await Access.ResolvePrincipalAsync(model.PrincipalType, model.PrincipalName, cancellationToken);
            var existing = target.Grants.FirstOrDefault(g => g.PrincipalType == model.PrincipalType && g.PrincipalId == id);
            model.FormActions = existing?.Actions ?? AccessAction.None;
        }

        return ScreenResult<PermissionDetailViewModel>.Ok(model);
    }

    /// <summary>Previews the form patterns (and the test package) without saving anything.</summary>
    public async Task<ScreenResult<PermissionDetailViewModel>> PreviewDetailAsync(AccessCaller caller, string? name, PermissionTargetForm form, CancellationToken cancellationToken)
    {
        var model = DetailModel(name, form);
        if (!await LoadAsync(model, caller, cancellationToken)) return ScreenResult<PermissionDetailViewModel>.NotFound();

        model.Preview = await RestrictToProjectAsync(model, await PreviewAsync(
            AccessPatterns.Split(model.RepositoryPatterns), AccessPatterns.Split(model.IncludePatterns), AccessPatterns.Split(model.ExcludePatterns), model.TestPackage, cancellationToken), cancellationToken);

        return ScreenResult<PermissionDetailViewModel>.Ok(model);
    }

    /// <summary>Replaces the target's patterns.</summary>
    public Task<ScreenResult<PermissionDetailViewModel>> SaveAsync(AccessCaller caller, string? name, PermissionTargetForm form, CancellationToken cancellationToken)
        => ActAsync<PermissionDetailViewModel>(caller, form.Password, confirm: true, async () =>
        {
            await Access.UpdateTargetAsync(
                name ?? string.Empty,
                AccessPatterns.Split(form.RepositoryPatterns),
                AccessPatterns.Split(form.IncludePatterns),
                AccessPatterns.Split(form.ExcludePatterns),
                Actor(caller),
                cancellationToken);

            return "The patterns were saved.";
        }, (message, error) => Back<PermissionDetailViewModel>(DetailScreen, Route(("name", name)), message, error));

    /// <summary>Gives a principal the ticked actions through the target, replacing an earlier grant to it.</summary>
    public Task<ScreenResult<PermissionDetailViewModel>> SetGrantAsync(AccessCaller caller, string? name, GrantForm form, CancellationToken cancellationToken)
        => ActAsync<PermissionDetailViewModel>(caller, form.Password, confirm: true, async () =>
        {
            var type = form.PrincipalType ?? PrincipalType.Group;
            var id = await Access.ResolvePrincipalAsync(type, form.PrincipalName ?? string.Empty, cancellationToken)
                     ?? throw new AccessModelException($"There is no {type.ToString().ToLowerInvariant()} named '{form.PrincipalName}'.");
            var granted = AccessPatternSupport.ParseActions(form.Actions);
            await Access.SetGrantAsync(name ?? string.Empty, type, id, granted, Actor(caller), cancellationToken);

            return granted == AccessAction.None
                ? $"{form.PrincipalName} holds nothing through this target any more."
                : $"{form.PrincipalName} now holds {AccessPatternSupport.Words(granted)} through this target.";
        }, (message, error) => Back<PermissionDetailViewModel>(DetailScreen, Route(("name", name)), message, error));

    /// <summary>Removes a principal's grant.</summary>
    public Task<ScreenResult<PermissionDetailViewModel>> RemoveGrantAsync(AccessCaller caller, string? name, PrincipalRemoval form, CancellationToken cancellationToken)
        => ActAsync<PermissionDetailViewModel>(caller, form.Password, confirm: true, async () =>
        {
            await Access.SetGrantAsync(name ?? string.Empty, form.Type, form.PrincipalId ?? string.Empty, AccessAction.None, Actor(caller), cancellationToken);

            return "The grant was removed.";
        }, (message, error) => Back<PermissionDetailViewModel>(DetailScreen, Route(("name", name)), message, error));

    /// <summary>Deletes the target and its grants; a project administrator is taken back to the project.</summary>
    public async Task<ScreenResult<PermissionDetailViewModel>> DeleteAsync(AccessCaller caller, string? name, PasswordForm form, CancellationToken cancellationToken)
    {
        var deleted = false;
        var project = (await Access.GetTargetAsync(name ?? string.Empty, cancellationToken))?.Target.ProjectName;

        return await ActAsync<PermissionDetailViewModel>(caller, form.Password, confirm: true, async () =>
        {
            await Access.DeleteTargetAsync(name ?? string.Empty, Actor(caller), cancellationToken);
            deleted = true;

            return $"Permission target {name} was deleted.";
        }, (message, error) =>
        {
            if (!deleted) return Back<PermissionDetailViewModel>(DetailScreen, Route(("name", name)), message, error);

            return caller.IsServerAdministrator || project == null
                ? Back<PermissionDetailViewModel>(ListScreen, null, message, error)
                : Back<PermissionDetailViewModel>(ScreenCatalog.AdminProjectDetail, Route(("name", project)), message, error);
        });
    }

    private static PermissionsViewModel ListModel(PermissionTargetForm form)
        => new()
        {
            Name = form.Name,
            RepositoryPatterns = form.RepositoryPatterns,
            IncludePatterns = form.IncludePatterns,
            ExcludePatterns = form.ExcludePatterns,
            Project = form.Project,
            TestPackage = form.TestPackage,
        };

    private static PermissionDetailViewModel DetailModel(string? name, PermissionTargetForm form)
        => new()
        {
            Name = name,
            RepositoryPatterns = form.RepositoryPatterns,
            IncludePatterns = form.IncludePatterns,
            ExcludePatterns = form.ExcludePatterns,
            TestPackage = form.TestPackage,
            PrincipalType = form.PrincipalType ?? PrincipalType.Group,
            PrincipalName = form.PrincipalName,
        };

    private async Task<PermissionsViewModel> LoadListAsync(PermissionsViewModel model, AccessCaller caller, CancellationToken cancellationToken)
    {
        model.IsServerAdministrator = caller.IsServerAdministrator;
        await LoadConfirmationAsync(model, caller);
        model.Targets = await Access.ListTargetsAsync(cancellationToken);
        model.Projects = await Access.ListProjectsAsync(cancellationToken);

        return model;
    }

    // A project target only ever applies to the project's repositories, so that is all the preview may show (and all a
    // project administrator may learn about).
    private async Task<PatternPreview> RestrictToProjectAsync(PermissionDetailViewModel model, PatternPreview preview, CancellationToken cancellationToken)
    {
        var projectName = model.Target?.Target.ProjectName;
        if (projectName == null) return preview;

        var project = await Access.GetProjectAsync(projectName, cancellationToken);
        var own = project?.Repositories ?? Array.Empty<string>();

        return preview with { Repositories = preview.Repositories.Where(r => own.Contains(r, StringComparer.Ordinal)).ToList() };
    }

    private async Task<bool> LoadAsync(PermissionDetailViewModel model, AccessCaller caller, CancellationToken cancellationToken)
    {
        model.IsServerAdministrator = caller.IsServerAdministrator;
        await LoadConfirmationAsync(model, caller);
        model.Target = await Access.GetTargetAsync(model.Name ?? string.Empty, cancellationToken);

        return model.Target != null;
    }
}
