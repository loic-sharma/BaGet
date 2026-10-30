using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Identity;
using BaGet.Identity.Application.Models.Repositories;

namespace BaGet.Identity.Application.UseCases.Admin;

/// <summary>
/// The screen that deletes a repository (<c>/admin/repositories/{name}/delete</c>) with no packages, and its history rows. The default repository
/// cannot be deleted. A repository that still holds packages is refused (delete the packages first), and so is one that a virtual repository lists as
/// a member (remove it there first). Refused with 503 while the <see cref="MaintenanceGate"/> is closed.
/// </summary>
public sealed class AdminRepositoryDeleteUseCase : AdminUseCaseBase
{
    private static readonly ScreenRef ListScreen = ScreenCatalog.AdminRepositories;

    private readonly IRepositoryAdministration _administration;
    private readonly IVirtualRepositoryAdministration _virtuals;

    /// <summary>Creates the use case.</summary>
    public AdminRepositoryDeleteUseCase(
        UserManager<BaGetUser> users,
        PasswordConfirmation confirmation,
        IRepositoryAdministration administration,
        IVirtualRepositoryAdministration virtuals,
        MaintenanceGate? gate = null)
        : base(users, confirmation, gate)
    {
        _administration = administration ?? throw new ArgumentNullException(nameof(administration));
        _virtuals = virtuals ?? throw new ArgumentNullException(nameof(virtuals));
    }

    /// <summary>Shows what the deletion does; not found when there is no such repository.</summary>
    public async Task<ScreenResult<RepositoryDeleteViewModel>> GetAsync(string? name, CancellationToken cancellationToken)
    {
        var model = new RepositoryDeleteViewModel { Name = name };
        model.Repository = await _administration.FindAsync(name, cancellationToken);
        if (model.Repository == null) return ScreenResult<RepositoryDeleteViewModel>.NotFound();

        model.ReferencedBy = await _virtuals.ReferencedByAsync(model.Repository.Name, cancellationToken);

        return ScreenResult<RepositoryDeleteViewModel>.Ok(model);
    }

    /// <summary>Deletes the repository, then returns to the list; a refusal shows the screen again with the reason.</summary>
    public async Task<ScreenResult<RepositoryDeleteViewModel>> DeleteAsync(string? name, CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        var model = new RepositoryDeleteViewModel { Name = name };
        model.Repository = await _administration.FindAsync(name, cancellationToken);
        if (model.Repository == null) return ScreenResult<RepositoryDeleteViewModel>.NotFound();

        try
        {
            await _administration.DeleteAsync(name!, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return ScreenResult<RepositoryDeleteViewModel>.NotFound();
        }
        catch (InvalidOperationException e) when (e is RepositoryProtectedException or RepositoryNotEmptyException or RepositoryReferencedException)
        {
            model.Error = e.Message;

            return ScreenResult<RepositoryDeleteViewModel>.Invalid(model, e.Message);
        }

        return Back<RepositoryDeleteViewModel>(ListScreen, null, $"Repository {name} was deleted.", null);
    }
}
