using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BaGet.Identity.Application.Models.Packages;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Identity.Application.UseCases.Packages;

/// <summary>
/// The two package listings of the administrator: the package IDs that have no owner in any repository (those that predate
/// accounts, and those left behind when their only owner's account was deleted; only administrators and the global API key may
/// change them), and the IDs one user owns.
/// </summary>
public sealed class AdminPackagesUseCase
{
    private readonly IContext _context;
    private readonly IRepositoryService _repositories;
    private readonly IServiceScopeFactory _scopes;
    private readonly PackageOwnershipService _ownership;
    private readonly UserManager<BaGetUser> _users;

    /// <summary>Creates the use case.</summary>
    public AdminPackagesUseCase(
        IContext context,
        IRepositoryService repositories,
        IServiceScopeFactory scopes,
        PackageOwnershipService ownership,
        UserManager<BaGetUser> users)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _users = users ?? throw new ArgumentNullException(nameof(users));
    }

    /// <summary>Lists the ownerless IDs of every repository, <paramref name="pageNumber"/> being one-based.</summary>
    public async Task<ScreenResult<AdminPackagesViewModel>> GetOwnerlessAsync(int pageNumber, CancellationToken cancellationToken)
    {
        var model = new AdminPackagesViewModel { PageNumber = pageNumber };

        // Ownership is per repository, so an ID is ownerless where nobody in that repository owns it.
        #pragma warning disable CS8602, CS8604
        var ownerless = _context.Packages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => !_context.PackageOwners.IgnoreQueryFilters()
                .Any(o => o.RepositoryKey == p.RepositoryKey && o.PackageId.ToLower() == p.Id.ToLower()))
            .Select(p => new { p.RepositoryKey, Id = p.Id.ToLower() })
            .Distinct();
        #pragma warning restore CS8602, CS8604

        var total = await ownerless.CountAsync(cancellationToken);
        model.PageCount = Math.Max(1, (total + AdminPackagesViewModel.PageSize - 1) / AdminPackagesViewModel.PageSize);
        model.PageNumber = Math.Min(Math.Max(1, model.PageNumber), model.PageCount);

        var page = await ownerless
            .OrderBy(o => o.Id)
            .ThenBy(o => o.RepositoryKey)
            .Skip((model.PageNumber - 1) * AdminPackagesViewModel.PageSize)
            .Take(AdminPackagesViewModel.PageSize)
            .ToListAsync(cancellationToken);

        var repositories = (await _repositories.ListAsync(cancellationToken)).ToDictionary(r => r.Key);

        // Each repository's rows are read in a scope pinned to it, which supplies its versions and owners.
        var rows = new List<AdminPackageRow>();
        foreach (var group in page.GroupBy(o => o.RepositoryKey))
        {
            if (!repositories.TryGetValue(group.Key, out var repository)) continue;

            using var scope = _scopes.CreateRepositoryScope(repository);
            rows.AddRange(await AdminPackageRows.BuildAsync(
                scope.ServiceProvider.GetRequiredService<IContext>(),
                group.Select(o => o.Id).ToList(),
                cancellationToken,
                repository.Name));
        }

        model.Packages = rows
            .OrderBy(r => r.Package.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.RepositoryName, StringComparer.Ordinal)
            .ToList();

        return ScreenResult<AdminPackagesViewModel>.Ok(model);
    }

    /// <summary>Lists the user's packages; not found for an unknown user.</summary>
    public async Task<ScreenResult<UserPackagesViewModel>> GetUserAsync(string? name, CancellationToken cancellationToken)
    {
        var user = await _users.FindByNameAsync(name ?? string.Empty);
        if (user == null) return ScreenResult<UserPackagesViewModel>.NotFound();

        var ids = await _ownership.GetOwnedPackageIdsAsync(user.Id, user.UserName, cancellationToken);

        return ScreenResult<UserPackagesViewModel>.Ok(new UserPackagesViewModel
        {
            Name = user.UserName,
            Packages = await AdminPackageRows.BuildAsync(_context, ids, cancellationToken),
        });
    }
}
