using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BaGet.Identity.Application.Services.Organizations;

namespace BaGet.Identity.Application.Services.Accounts;

/// <summary>
/// Atomically deletes a user together with what hangs off them: API keys, organization memberships
/// and package ownerships. The ownership rows go too, because they are keyed by name and a
/// later user registering the same name would otherwise inherit the packages.
/// </summary>
public class AccountDeletionService
{
    private readonly IContext _context;
    private readonly UserManager<BaGetUser> _users;
    private readonly IServiceScopeFactory _scopes;
    private readonly IRepositoryService _repositories;
    private readonly ILogger<AccountDeletionService> _logger;

    public AccountDeletionService(
        IContext context,
        UserManager<BaGetUser> users,
        IServiceScopeFactory scopes,
        IRepositoryService repositories,
        ILogger<AccountDeletionService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DeletionBlockers> GetBlockersAsync(BaGetUser? user, CancellationToken cancellationToken)
    {
        #pragma warning disable CS8602
        var name = user.UserName?.ToLowerInvariant();
        #pragma warning restore CS8602

        #pragma warning disable CS8602, CS8604
        // Ownership is per repository, and an account belongs to the whole server: look in every repository.
        var owned = await _context.PackageOwners
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(o => o.OwnerName.ToLower() == name)
            .Select(o => new { o.RepositoryKey, o.PackageId })
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8602, CS8604

        var lastOwner = new List<(int RepositoryKey, string? PackageId)>();
        foreach (var entry in owned
            .GroupBy(o => (o.RepositoryKey, Id: o.PackageId?.ToLowerInvariant()))
            .Select(g => g.First())
            .OrderBy(o => o.PackageId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.RepositoryKey))
        {
            var id = entry.PackageId?.ToLowerInvariant();
            var repositoryKey = entry.RepositoryKey;
            #pragma warning disable CS8602
            if (await _context.PackageOwners.IgnoreQueryFilters().CountAsync(
                    o => o.RepositoryKey == repositoryKey && o.PackageId.ToLower() == id, cancellationToken) == 1)
            #pragma warning restore CS8602
            {
                lastOwner.Add((repositoryKey, entry.PackageId));
            }
        }

        var blocking = new List<string?>();
        foreach (var organization in await OrganizationsToLeaveAsync(user, cancellationToken))
        {
            #pragma warning disable CS8602
            var blocked = organization.OtherMembers > 0
                ? organization.IsLastAdmin
                : await OrganizationOwnsPackagesAsync(organization.Name, cancellationToken);
            #pragma warning restore CS8602

            if (blocked) blocking.Add(organization.Name);
        }

        // Same rule as AdministratorService.IsLastAdministratorAsync.
        var lastAdministrator = user.IsAdministrator
            && await _context.Users.CountAsync(u => u.IsAdministrator, cancellationToken) <= 1;

        var known = lastOwner.Count == 0
            ? new Dictionary<int, RepositoryInfo>()
            : (await _repositories.ListAsync(cancellationToken)).ToDictionary(r => r.Key);

        return new DeletionBlockers(
            lastOwner.Select(o => o.PackageId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            blocking,
            lastAdministrator,
            lastOwner
                .Select(o => new SoleOwnedPackage(
                    o.RepositoryKey,
                    known.TryGetValue(o.RepositoryKey, out var found) ? found.Name : Repository.DefaultName,
                    o.PackageId,
                    found?.Format ?? RepositoryFormat.NuGet))
                .ToList());
    }

    /// <summary>
    /// Delete the user, or return the reasons it cannot be done.
    /// </summary>
    public async Task<(DeletionBlockers Blockers, IdentityResult Result)> DeleteAsync(BaGetUser? user, CancellationToken cancellationToken)
    {
        var blockers = await GetBlockersAsync(user, cancellationToken);
        #pragma warning disable CS8619
        if (blockers.Any) return (blockers, null);
        #pragma warning restore CS8619

        await StageCleanupAsync(user, administrator: false, cancellationToken);

        return (blockers, await SaveDeletionAsync(user));
    }

    /// <summary>
    /// Delete another user as an administrator. Unlike <see cref="DeleteAsync"/> nothing but
    /// <see cref="DeletionBlockers.LastAdministrator"/> blocks it: an organization whose last
    /// administrator leaves hands the role to its longest-standing member, an organization of one
    /// that owns packages is kept without members, and the IDs only this user owned are either
    /// left without owners or deleted. The package deletion runs after the user is gone and
    /// never undoes it: failures are returned.
    /// </summary>
    /// <param name="user">The user to delete.</param>
    /// <param name="deleteSoleOwnedPackages">Delete the sole-owned IDs with all their versions instead of leaving them ownerless.</param>
    /// <param name="actor">The administrator, for the log.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<AdminDeletionResult> DeleteByAdministratorAsync(
        BaGetUser? user, bool deleteSoleOwnedPackages, string? actor, CancellationToken cancellationToken)
    {
        var none = Array.Empty<string>();

        var blockers = await GetBlockersAsync(user, cancellationToken);
        if (blockers.LastAdministrator)
        {
            return new AdminDeletionResult(
                IdentityResult.Failed(new IdentityError { Description = "The last administrator cannot be deleted. Promote another user first." }),
                none, none, none);
        }

        // The IDs only this user owned, in every repository: each is removed from its own repository.
        var soleOwned = blockers.LastOwnerPackages;
        var userName = user?.UserName;

        await StageCleanupAsync(user, administrator: true, cancellationToken);
        var result = await SaveDeletionAsync(user);
        if (!result.Succeeded) return new AdminDeletionResult(result, none, none, none);

        var deleted = new List<string>();
        var failures = new List<string?>();
        if (deleteSoleOwnedPackages)
        {
            var repositories = (await _repositories.ListAsync(cancellationToken)).ToDictionary(r => r.Key);
            foreach (var package in soleOwned)
            {
                try
                {
                    if (!repositories.TryGetValue(package.RepositoryKey, out var repository))
                    {
                        failures.Add(package.Label);
                        continue;
                    }

                    // The deletion services work in the repository their scope is pinned to.
                    using var scope = _scopes.CreateRepositoryScope(repository);

                    if (repository.Format == RepositoryFormat.Npm)
                    {
                        // An npm package is unpublished whole, as the administrator the deletion is done by.
                        var administrator = new BaGetPrincipal(null, actor, isSuperuser: false, ApiKeyScopes.All, "*", isAdministrator: true);
                        var outcome = await scope.ServiceProvider.GetRequiredService<INpmRegistry>()
                            .UnpublishPackageAsync(administrator, package.PackageId?.ToLowerInvariant() ?? string.Empty, cancellationToken);
                        if (outcome.Succeeded || outcome.Status == NpmStatus.NotFound) deleted.Add(package.Label); else failures.Add(package.Label);

                        continue;
                    }

                    if (repository.Format == RepositoryFormat.Maven)
                    {
                        // A Maven artifact is deleted whole (every version and file), as the administrator the deletion is done by.
                        var administrator = new BaGetPrincipal(null, actor, isSuperuser: false, ApiKeyScopes.All, "*", isAdministrator: true);
                        var deletedArtifact = MavenNames.TryParseArtifactName(package.PackageId, out var mavenGroup, out var mavenArtifact)
                            ? await scope.ServiceProvider.GetRequiredService<IMavenRepository>().DeleteArtifactAsync(administrator, mavenGroup, mavenArtifact, cancellationToken)
                            : null;
                        if (deletedArtifact != null && (deletedArtifact.Succeeded || deletedArtifact.Status == MavenStatus.NotFound)) deleted.Add(package.Label); else failures.Add(package.Label);

                        continue;
                    }

                    var packages = scope.ServiceProvider.GetRequiredService<IPackageDatabase>();
                    var deletion = scope.ServiceProvider.GetRequiredService<IPackageDeletionService>();

                    var versions = await packages.FindAsync(package.PackageId, includeUnlisted: true, cancellationToken);
                    var failed = false;
                    foreach (var version in versions)
                    {
                        failed |= !await deletion.TryHardDeletePackageAsync(package.PackageId, version.Version, cancellationToken);
                    }

                    if (failed) failures.Add(package.Label); else deleted.Add(package.Label);
                }
                catch (Exception e) when (e is not OperationCanceledException and not MaintenanceException)
                {
                    _logger.LogError(e, "Could not delete package {Id} ({Repository}) of deleted user {User}", package.PackageId, package.RepositoryName, userName);
                    failures.Add(package.Label);
                }
            }
        }

        var kept = deleteSoleOwnedPackages
            ? new List<string>()
            : soleOwned.Select(p => p.Label).ToList();
        _logger.LogWarning(
            "{Admin} deleted user {User}; sole-owned IDs {Ids} {Outcome}",
            actor, userName, string.Join(", ", soleOwned.Select(p => p.Label)), deleteSoleOwnedPackages ? "deleted" : "kept without owners");

        return new AdminDeletionResult(result, kept, deleted, failures);
    }

    /// <summary>What <see cref="DeleteByAdministratorAsync"/> would do to <paramref name="user"/>, for the confirmation page.</summary>
    public async Task<AdminDeletionPreview> PreviewAdministratorDeletionAsync(BaGetUser user, CancellationToken cancellationToken)
    {
        var name = user.UserName?.ToLowerInvariant();
        var blockers = await GetBlockersAsync(user, cancellationToken);

        #pragma warning disable CS8602, CS8604
        var direct = (await _context.PackageOwners
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(o => o.OwnerName.ToLower() == name)
                .Select(o => o.PackageId)
                .ToListAsync(cancellationToken))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(i => i, StringComparer.OrdinalIgnoreCase)
            .ToList();
        #pragma warning restore CS8602, CS8604

        var outcomes = new List<OrganizationOutcome>();
        foreach (var organization in await OrganizationsToLeaveAsync(user, cancellationToken))
        {
            string outcome;
            if (organization.OtherMembers == 0)
            {
                #pragma warning disable CS8602
                outcome = await OrganizationOwnsPackagesAsync(organization.Name, cancellationToken)
                    ? "The organization owns packages, so it is kept without members."
                    : "The organization has no other member and is deleted.";
                #pragma warning restore CS8602
            }
            else if (organization.IsLastAdmin)
            {
                #pragma warning disable CS8604
                var successor = await _context.OrganizationMembers
                    .AsNoTracking()
                    .Where(m => m.OrganizationKey == organization.Key && m.UserId != user.Id)
                    .OrderBy(m => m.Key)
                    .Select(m => m.UserId)
                    .FirstAsync(cancellationToken);
                var successorName = (await _users.FindByIdAsync(successor))?.UserName ?? successor;
                #pragma warning restore CS8604

                outcome = $"{successorName}, its longest-standing member, becomes its administrator.";
            }
            else
            {
                outcome = "The user leaves the organization.";
            }

            outcomes.Add(new OrganizationOutcome(organization.Name, outcome));
        }

        #pragma warning disable CS8602
        var keys = await _context.ApiKeys?.CountAsync(k => k.UserId == user.Id, cancellationToken);
        #pragma warning restore CS8602

        return new AdminDeletionPreview(keys, direct, blockers.LastOwnerPackages, outcomes);
    }

    /// <summary>
    /// Stage the removal of the user's API keys, ownerships and memberships; nothing is saved
    /// until the user itself is deleted.
    /// </summary>
    /// <param name="user">The user being deleted.</param>
    /// <param name="administrator">
    /// An administrator is deleting: the last administrator of an organization with other members
    /// is replaced by its longest-standing member, and an organization of one that owns packages
    /// is kept. Otherwise those cases were refused beforehand.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private async Task StageCleanupAsync(BaGetUser? user, bool administrator, CancellationToken cancellationToken)
    {
        #pragma warning disable CS8602
        var keys = await _context.ApiKeys?.Where(k => k.UserId == user.Id).ToListAsync(cancellationToken);
        #pragma warning restore CS8602
        _context.ApiKeys?.RemoveRange(keys);

        var name = user?.UserName?.ToLowerInvariant();
        #pragma warning disable CS8602
        var ownerships = await _context.PackageOwners?.IgnoreQueryFilters().Where(o => o.OwnerName.ToLower() == name).ToListAsync(cancellationToken);
        #pragma warning restore CS8602
        _context.PackageOwners?.RemoveRange(ownerships);

        await StageAccessCleanupAsync(PrincipalType.User, user?.Id, cancellationToken);

        // A membership of no organization (OrganizationKey is optional, a legacy row) is not found by OrganizationsToLeaveAsync, but
        // OrganizationMember.UserId is a foreign key to the user.
        #pragma warning disable CS8602
        _context.OrganizationMembers.RemoveRange(await _context.OrganizationMembers.Where(m => m.UserId == user.Id && m.OrganizationKey == null).ToListAsync(cancellationToken));
        #pragma warning restore CS8602

        foreach (var organization in await OrganizationsToLeaveAsync(user, cancellationToken))
        {
            #pragma warning disable CS8604
            var members = await _context.OrganizationMembers
                .Where(m => m.OrganizationKey == organization.Key)
                .ToListAsync(cancellationToken);
            #pragma warning restore CS8604

            if (administrator && organization.OtherMembers > 0 && organization.IsLastAdmin)
            {
                members.Where(m => m.UserId != user?.Id).OrderBy(m => m.Key).First().IsAdmin = true;
            }

            // An organization of one with nothing to its name goes with its only member; an
            // administrator's deletion keeps one that owns packages, without members.
            #pragma warning disable CS8602
            var removeOrganization = organization.OtherMembers == 0
                && !(administrator && await OrganizationOwnsPackagesAsync(organization.Name, cancellationToken));
            #pragma warning restore CS8602
            _context.OrganizationMembers.RemoveRange(removeOrganization ? members : members.Where(m => m.UserId == user?.Id));
            if (removeOrganization)
            {
                await StageAccessCleanupAsync(PrincipalType.Organization, organization.Key.ToString(), cancellationToken);
                #pragma warning disable CS8602
                _context.Organizations?.RemoveRange(await _context.Organizations?.Where(o => o.Key == organization.Key).ToListAsync(cancellationToken));
                #pragma warning restore CS8602
            }
        }
    }

    // A deleted user, or organization, leaves the access model with them: group memberships (users only), permission
    // grants and project roles that name them. Orphans would be harmless to a decision (nobody can present that
    // identity again) but would linger in the admin pages. Staged with the rest of the cleanup.
    private Task StageAccessCleanupAsync(PrincipalType type, string? id, CancellationToken cancellationToken)
        => AccessAdministration.StageCleanupAsync(_context, type, id, cancellationToken);

    // The cleanup is only staged. Deleting the user saves it in the same SaveChanges, so the
    // database applies all of it or none of it: a user that cannot be deleted keeps its keys,
    // ownerships and memberships.
    private async Task<IdentityResult> SaveDeletionAsync(BaGetUser? user)
    {
        try
        {
            #pragma warning disable CS8604
            var result = await _users.DeleteAsync(user);
            #pragma warning restore CS8604
            if (!result.Succeeded) Discard();

            return result;
        }
        catch
        {
            Discard();
            throw;
        }
    }

    /// <summary>
    /// Forget the staged cleanup, so a later save in the same scope cannot apply it.
    /// </summary>
    private void Discard()
    {
        if (_context is DbContext context) context.ChangeTracker.Clear();
    }

    private async Task<IReadOnlyList<OrganizationMembership>> OrganizationsToLeaveAsync(BaGetUser? user, CancellationToken cancellationToken)
    {
        #pragma warning disable CS8602, CS8604
        var mine = await _context.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.UserId == user.Id && m.Organization != null)
            .Select(m => new { m.OrganizationKey, m.IsAdmin, m.Organization.Name })
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8602, CS8604

        var result = new List<OrganizationMembership>();
        foreach (var membership in mine)
        {
            #pragma warning disable CS8602
            var others = await _context.OrganizationMembers
                .AsNoTracking()
                .Where(m => m.OrganizationKey == membership.OrganizationKey && m.UserId != user.Id)
                .Select(m => m.IsAdmin)
                .ToListAsync(cancellationToken);
            #pragma warning restore CS8602

            result.Add(new OrganizationMembership(
                membership.OrganizationKey.GetValueOrDefault(),
                membership.Name,
                membership.IsAdmin && !others.Any(isAdmin => isAdmin),
                others.Count));
        }

        return result;
    }

    private Task<bool>? OrganizationOwnsPackagesAsync(string? organization, CancellationToken cancellationToken)
    {
        var name = organization?.ToLowerInvariant();

        #pragma warning disable CS8602
        return _context.PackageOwners?.IgnoreQueryFilters().AnyAsync(o => o.OwnerName.ToLower() == name, cancellationToken);
        #pragma warning restore CS8602
    }
}
