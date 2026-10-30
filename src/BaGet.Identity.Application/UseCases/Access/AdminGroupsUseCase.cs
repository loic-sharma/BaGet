using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Identity;
using BaGet.Identity.Application.Models.Access;
using BaGet.Identity.Application.Models.Shared;

namespace BaGet.Identity.Application.UseCases.Access;

/// <summary>
/// The groups screens of the administrator: the list with the new-group form, and one group (rename, members found by a user-name search,
/// delete). A group is a named set of users that permission targets and project roles can name; the built-in <c>Everyone</c> and
/// <c>Authenticated</c> have implicit members and cannot be renamed or deleted. Membership changes and deletion ask for the password again; every
/// change is refused with 503 while the <see cref="MaintenanceGate"/> is closed and goes through <see cref="IAccessAdministration"/>, which audits it.
/// </summary>
public sealed class AdminGroupsUseCase : AccessUseCaseBase
{
    /// <summary>How many users a member search lists.</summary>
    public const int SearchSize = 20;

    private static readonly ScreenRef ListScreen = ScreenCatalog.AdminGroups;
    private static readonly ScreenRef DetailScreen = ScreenCatalog.AdminGroupDetail;

    /// <summary>Creates the use case.</summary>
    public AdminGroupsUseCase(
        UserManager<BaGetUser> users,
        PasswordConfirmation confirmation,
        IAccessAdministration access,
        IRepositoryService repositories,
        MaintenanceGate? gate = null)
        : base(users, confirmation, access, repositories, gate)
    {
    }

    /// <summary>Lists the groups.</summary>
    public async Task<ScreenResult<GroupsViewModel>> ListAsync(AccessCaller caller, CancellationToken cancellationToken)
        => ScreenResult<GroupsViewModel>.Ok(await LoadListAsync(new GroupsViewModel(), caller, cancellationToken));

    /// <summary>Creates a group and opens it; a refusal shows the list with the reason.</summary>
    public async Task<ScreenResult<GroupsViewModel>> CreateAsync(AccessCaller caller, GroupCreation form, CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        var model = new GroupsViewModel { Name = form.Name, Description = form.Description };
        try
        {
            var group = await Access.CreateGroupAsync(form.Name ?? string.Empty, form.Description, Actor(caller), cancellationToken);

            return Back<GroupsViewModel>(DetailScreen, Route(("name", group.Name)), $"Group {group.Name} was created.", null);
        }
        catch (AccessModelException e)
        {
            model.Error = e.Message;

            return ScreenResult<GroupsViewModel>.Ok(await LoadListAsync(model, caller, cancellationToken));
        }
    }

    /// <summary>Shows the group, with the member search for <paramref name="q"/>; not found when there is none.</summary>
    public async Task<ScreenResult<GroupDetailViewModel>> GetAsync(AccessCaller caller, string? name, string? q, CancellationToken cancellationToken)
    {
        var model = new GroupDetailViewModel { Name = name, Q = q };

        return await LoadAsync(model, caller, cancellationToken)
            ? ScreenResult<GroupDetailViewModel>.Ok(model)
            : ScreenResult<GroupDetailViewModel>.NotFound();
    }

    /// <summary>Renames the group.</summary>
    public Task<ScreenResult<GroupDetailViewModel>> RenameAsync(AccessCaller caller, string? name, GroupRename form, CancellationToken cancellationToken)
    {
        var current = name;

        return ActAsync<GroupDetailViewModel>(caller, null, confirm: false, async () =>
        {
            await Access.RenameGroupAsync(name ?? string.Empty, form.NewName ?? string.Empty, Actor(caller), cancellationToken);
            current = form.NewName;

            return $"Group {name} is now called {form.NewName}.";
        }, (message, error) => Back<GroupDetailViewModel>(DetailScreen, Route(("name", current)), message, error));
    }

    /// <summary>Adds a user to the group.</summary>
    public Task<ScreenResult<GroupDetailViewModel>> AddMemberAsync(AccessCaller caller, string? name, GroupMemberChange form, CancellationToken cancellationToken)
        => ActAsync<GroupDetailViewModel>(caller, form.Password, confirm: true, async () =>
        {
            await Access.AddGroupMemberAsync(name ?? string.Empty, form.UserId ?? string.Empty, Actor(caller), cancellationToken);

            return "The member was added.";
        }, (message, error) => Back<GroupDetailViewModel>(DetailScreen, Route(("name", name)), message, error));

    /// <summary>Removes a user from the group.</summary>
    public Task<ScreenResult<GroupDetailViewModel>> RemoveMemberAsync(AccessCaller caller, string? name, GroupMemberChange form, CancellationToken cancellationToken)
        => ActAsync<GroupDetailViewModel>(caller, form.Password, confirm: true, async () =>
        {
            await Access.RemoveGroupMemberAsync(name ?? string.Empty, form.UserId ?? string.Empty, Actor(caller), cancellationToken);

            return "The member was removed.";
        }, (message, error) => Back<GroupDetailViewModel>(DetailScreen, Route(("name", name)), message, error));

    /// <summary>Deletes the group with the grants and project roles that name it; on success the list is shown with the outcome.</summary>
    public Task<ScreenResult<GroupDetailViewModel>> DeleteAsync(AccessCaller caller, string? name, PasswordForm form, CancellationToken cancellationToken)
    {
        var deleted = false;

        return ActAsync<GroupDetailViewModel>(caller, form.Password, confirm: true, async () =>
        {
            await Access.DeleteGroupAsync(name ?? string.Empty, Actor(caller), cancellationToken);
            deleted = true;

            return $"Group {name} was deleted.";
        }, (message, error) => deleted
            ? Back<GroupDetailViewModel>(ListScreen, null, message, error)
            : Back<GroupDetailViewModel>(DetailScreen, Route(("name", name)), message, error));
    }

    private async Task<GroupsViewModel> LoadListAsync(GroupsViewModel model, AccessCaller caller, CancellationToken cancellationToken)
    {
        model.IsServerAdministrator = caller.IsServerAdministrator;
        model.Groups = await Access.ListGroupsAsync(cancellationToken);

        return model;
    }

    private async Task<bool> LoadAsync(GroupDetailViewModel model, AccessCaller caller, CancellationToken cancellationToken)
    {
        model.IsServerAdministrator = caller.IsServerAdministrator;
        await LoadConfirmationAsync(model, caller);

        model.Group = await Access.GetGroupAsync(model.Name ?? string.Empty, cancellationToken);
        if (model.Group == null) return false;

        if (!model.Group.Group.IsBuiltIn && !string.IsNullOrWhiteSpace(model.Q))
        {
            var members = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in model.Group.Members) members.Add(member.UserId);

            var found = await Access.SearchUsersAsync(model.Q, SearchSize, cancellationToken);
            var fresh = new List<GroupMemberInfo>();
            foreach (var user in found)
            {
                if (!members.Contains(user.UserId)) fresh.Add(user);
            }

            model.Found = fresh;
        }

        return true;
    }
}
