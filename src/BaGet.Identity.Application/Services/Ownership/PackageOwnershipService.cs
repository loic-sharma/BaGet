using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using BaGet.Identity.Application.Services.Organizations;

namespace BaGet.Identity.Application.Services.Ownership;

/// <summary>
/// Package owners, and the organizations users own packages through.
/// </summary>
public class PackageOwnershipService
{
    private readonly IContext _context;
    private readonly SystemTime _time;

    public PackageOwnershipService(IContext context, SystemTime time)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public async Task<IReadOnlyList<PackageOwner>> GetOwnersAsync(string? packageId, CancellationToken cancellationToken)
    {
        var id = packageId?.ToLowerInvariant();

        #pragma warning disable CS8602, CS8604
        return await _context.PackageOwners
            .AsNoTracking()
            .Where(o => o.PackageId.ToLower() == id)
            .OrderBy(o => o.OwnerName)
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8602, CS8604
    }

    /// <summary>
    /// The account names a user acts as: their own and every organization they belong to.
    /// </summary>
    public async Task<IReadOnlyList<string>?> GetAccountNamesAsync(string? userId, string? userName, CancellationToken cancellationToken)
    {
        #pragma warning disable CS8602, CS8604
        var organizations = await _context.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.Organization != null)
            .Select(m => m.Organization.Name)
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8602, CS8604

        #pragma warning disable CS8619
        return new[] { userName }.Concat(organizations).ToList();
        #pragma warning restore CS8619
    }

    public async Task<bool> IsOwnerAsync(string? userId, string? userName, string? packageId, CancellationToken cancellationToken)
    {
        #pragma warning disable CS8604
        var names = (await GetAccountNamesAsync(userId, userName, cancellationToken))
            .Select(n => n.ToLowerInvariant())
            .ToList();
        #pragma warning restore CS8604
        var id = packageId?.ToLowerInvariant();

        #pragma warning disable CS8602, CS8604
        return await _context.PackageOwners
            .AnyAsync(o => o.PackageId.ToLower() == id && names.Contains(o.OwnerName.ToLower()), cancellationToken);
        #pragma warning restore CS8602, CS8604
    }

    /// <summary>
    /// The package IDs the user owns, directly or through an organization.
    /// </summary>
    public async Task<IReadOnlyList<string>?> GetOwnedPackageIdsAsync(string? userId, string? userName, CancellationToken cancellationToken)
    {
        #pragma warning disable CS8604
        var names = (await GetAccountNamesAsync(userId, userName, cancellationToken))
            .Select(n => n.ToLowerInvariant())
            .ToList();
        #pragma warning restore CS8604

        #pragma warning disable CS8602, CS8604
        var ids = await _context.PackageOwners
            .AsNoTracking()
            .Where(o => names.Contains(o.OwnerName.ToLower()))
            .Select(o => o.PackageId)
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8602, CS8604

        #pragma warning disable CS8619
        return ids.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(i => i, StringComparer.OrdinalIgnoreCase).ToList();
        #pragma warning restore CS8619
    }

    public async Task<bool> HasOwnersAsync(string? packageId, CancellationToken cancellationToken)
    {
        var id = packageId?.ToLowerInvariant();

        #pragma warning disable CS8602
        return await _context.PackageOwners?.AnyAsync(o => o.PackageId.ToLower() == id, cancellationToken);
        #pragma warning restore CS8602
    }

    public async Task<bool> AccountExistsAsync(string? name, CancellationToken cancellationToken)
    {
        var normalized = name?.ToUpperInvariant();
        var lower = name?.ToLowerInvariant();

        #pragma warning disable CS8602
        return await _context.Users.AnyAsync(u => u.NormalizedUserName == normalized, cancellationToken)
               || await _context.Organizations?.AnyAsync(o => o.Name.ToLower() == lower, cancellationToken);
        #pragma warning restore CS8602
    }

    public async Task<OwnerChangeResult> AddOwnerAsync(string? packageId, string? ownerName, CancellationToken cancellationToken)
    {
        if (!await AccountExistsAsync(ownerName, cancellationToken)) return OwnerChangeResult.UnknownAccount;

        var owners = await GetOwnersAsync(packageId, cancellationToken);
        if (owners.Any(o => string.Equals(o.OwnerName, ownerName, StringComparison.OrdinalIgnoreCase)))
        {
            return OwnerChangeResult.AlreadyOwner;
        }

        _context.PackageOwners?.Add(new PackageOwner { PackageId = packageId, OwnerName = ownerName });
        await _context.SaveChangesAsync(cancellationToken);

        return OwnerChangeResult.Success;
    }

    public async Task<OwnerChangeResult> RemoveOwnerAsync(string? packageId, string ownerName, CancellationToken cancellationToken)
    {
        var id = packageId?.ToLowerInvariant();
        #pragma warning disable CS8602, CS8604
        var owners = await _context.PackageOwners
            .Where(o => o.PackageId.ToLower() == id)
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8602, CS8604

        var owner = owners.FirstOrDefault(o => string.Equals(o.OwnerName, ownerName, StringComparison.OrdinalIgnoreCase));
        if (owner == null) return OwnerChangeResult.NotOwner;
        if (owners.Count == 1) return OwnerChangeResult.LastOwner;

        _context.PackageOwners.Remove(owner);
        await _context.SaveChangesAsync(cancellationToken);

        return OwnerChangeResult.Success;
    }

    /// <summary>
    /// Mark or unmark the ID as reserved for its owners. Only the superuser may do this.
    /// </summary>
    public async Task<bool> SetVerifiedAsync(string packageId, bool verified, CancellationToken cancellationToken)
    {
        var id = packageId.ToLowerInvariant();
        #pragma warning disable CS8602, CS8604
        var owners = await _context.PackageOwners
            .Where(o => o.PackageId.ToLower() == id)
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8602, CS8604

        if (owners.Count == 0) return false;

        owners.ForEach(o => o.IsVerified = verified);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> IsVerifiedAsync(string packageId, CancellationToken cancellationToken)
    {
        var id = packageId.ToLowerInvariant();

        #pragma warning disable CS8602
        return await _context.PackageOwners?.AnyAsync(o => o.PackageId.ToLower() == id && o.IsVerified, cancellationToken);
        #pragma warning restore CS8602
    }

    public async Task<IReadOnlyList<Organization>?> GetOrganizationsAsync(string? userId, CancellationToken cancellationToken)
    {
        #pragma warning disable CS8602, CS8604, CS8619
        return await _context.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.Organization != null)
            .Select(m => m.Organization)
            .OrderBy(o => o.Name)
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8602, CS8604, CS8619
    }

    /// <summary>
    /// Create an organization with its creator as the first administrator. False if
    /// the name is already a user or an organization.
    /// </summary>
    public async Task<bool> CreateOrganizationAsync(string name, string? creatorUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || await AccountExistsAsync(name, cancellationToken)) return false;

        var organization = new Organization { Name = name.Trim(), Created = _time.UtcNow };
        _context.Organizations?.Add(organization);
        _context.OrganizationMembers?.Add(new OrganizationMember
        {
            Organization = organization,
            UserId = creatorUserId,
            IsAdmin = true,
        });

        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<OrganizationMember>> GetMembersAsync(string? organizationName, CancellationToken cancellationToken)
    {
        var name = organizationName?.ToLowerInvariant();

        #pragma warning disable CS8602, CS8604
        return await _context.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.Organization != null && m.Organization.Name.ToLower() == name)
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8602, CS8604
    }

    public async Task<bool> IsOrganizationAdminAsync(string? organizationName, string? userId, CancellationToken cancellationToken)
    {
        var members = await GetMembersAsync(organizationName, cancellationToken);

        return members.Any(m => m.UserId == userId && m.IsAdmin);
    }

    public async Task<bool> AddMemberAsync(string organizationName, string userId, bool isAdmin, CancellationToken cancellationToken)
    {
        var name = organizationName.ToLowerInvariant();
        #pragma warning disable CS8602
        var organization = await _context.Organizations?.FirstOrDefaultAsync(o => o.Name.ToLower() == name, cancellationToken);
        #pragma warning restore CS8602
        if (organization == null) return false;

        var members = await GetMembersAsync(organizationName, cancellationToken);
        if (members.Any(m => m.UserId == userId)) return false;

        _context.OrganizationMembers?.Add(new OrganizationMember
        {
            OrganizationKey = organization.Key,
            UserId = userId,
            IsAdmin = isAdmin,
        });
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Remove a member. The last administrator cannot be removed.
    /// </summary>
    public async Task<bool> RemoveMemberAsync(string organizationName, string userId, CancellationToken cancellationToken)
    {
        var name = organizationName.ToLowerInvariant();
        #pragma warning disable CS8602, CS8604
        var members = await _context.OrganizationMembers
            .Where(m => m.Organization != null && m.Organization.Name.ToLower() == name)
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8602, CS8604

        var member = members.FirstOrDefault(m => m.UserId == userId);
        if (member == null) return false;
        if (member.IsAdmin && members.Count(m => m.IsAdmin) == 1) return false;

        _context.OrganizationMembers.Remove(member);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Every organization with its member and owned-package counts, by name. Ownership is counted in
    /// every repository.
    /// </summary>
    public async Task<IReadOnlyList<OrganizationSummary>> GetAllOrganizationsAsync(CancellationToken cancellationToken)
    {
        #pragma warning disable CS8602, CS8604
        var organizations = await _context.Organizations.AsNoTracking().OrderBy(o => o.Name).ToListAsync(cancellationToken);
        var members = await _context.OrganizationMembers.AsNoTracking()
            .Where(m => m.OrganizationKey != null)
            .Select(m => new { m.OrganizationKey, m.IsAdmin })
            .ToListAsync(cancellationToken);
        var owned = await _context.PackageOwners.IgnoreQueryFilters().AsNoTracking()
            .Select(o => new { o.OwnerName, o.RepositoryKey, o.PackageId })
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8602, CS8604

        var ownedBy = owned
            .GroupBy(o => o.OwnerName?.ToLowerInvariant() ?? string.Empty)
            .ToDictionary(g => g.Key, g => g.Select(o => (o.RepositoryKey, o.PackageId?.ToLowerInvariant())).Distinct().Count());

        return organizations
            .Select(o => new OrganizationSummary(
                o.Name,
                o.Created,
                members.Count(m => m.OrganizationKey == o.Key),
                members.Count(m => m.OrganizationKey == o.Key && m.IsAdmin),
                ownedBy.GetValueOrDefault(o.Name?.ToLowerInvariant() ?? string.Empty)))
            .ToList();
    }

    /// <summary>The organization with that name (any case), or null.</summary>
    public async Task<Organization?> FindOrganizationAsync(string? name, CancellationToken cancellationToken)
    {
        var lower = name?.ToLowerInvariant();

        #pragma warning disable CS8602, CS8604
        return await _context.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Name.ToLower() == lower, cancellationToken);
        #pragma warning restore CS8602, CS8604
    }

    /// <summary>
    /// The package IDs the account owns, counted per repository (every repository, not the request's).
    /// </summary>
    public async Task<int> CountOwnedPackageIdsAsync(string? ownerName, CancellationToken cancellationToken)
    {
        var name = ownerName?.ToLowerInvariant();

        #pragma warning disable CS8602, CS8604
        var owned = await _context.PackageOwners.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.OwnerName.ToLower() == name)
            .Select(o => new { o.RepositoryKey, o.PackageId })
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8602, CS8604

        return owned.Select(o => (o.RepositoryKey, o.PackageId?.ToLowerInvariant())).Distinct().Count();
    }

    /// <summary>
    /// Rename an organization. Every <see cref="PackageOwner"/> row naming it, in every repository, follows in the
    /// same save, so ownership is never split between the two names. The new name must be free: users and
    /// organizations share one namespace (the organization's own name is free, so a change of case works).
    /// </summary>
    public async Task<OrganizationChange> RenameOrganizationAsync(string? name, string? newName, CancellationToken cancellationToken)
    {
        var lower = name?.ToLowerInvariant();
        #pragma warning disable CS8602, CS8604
        var organization = await _context.Organizations.FirstOrDefaultAsync(o => o.Name.ToLower() == lower, cancellationToken);
        #pragma warning restore CS8602, CS8604
        if (organization == null) return OrganizationChange.NotFound;

        var trimmed = newName?.Trim();
        if (string.IsNullOrEmpty(trimmed)
            || trimmed.Length > AbstractContext<DbContext>.MaxAccountNameLength
            || trimmed.Any(c => char.IsControl(c) || UnsafeNameCharacters.Contains(c)))
        {
            return OrganizationChange.InvalidName;
        }

        var taken = await AccountExistsAsync(trimmed, cancellationToken)
                    && !string.Equals(trimmed, organization.Name, StringComparison.OrdinalIgnoreCase);
        if (taken) return OrganizationChange.NameTaken;

        var oldLower = organization.Name?.ToLowerInvariant();
        #pragma warning disable CS8602, CS8604
        var owners = await _context.PackageOwners.IgnoreQueryFilters()
            .Where(o => o.OwnerName.ToLower() == oldLower)
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8602, CS8604

        organization.Name = trimmed;
        foreach (var owner in owners) owner.OwnerName = trimmed;

        await _context.SaveChangesAsync(cancellationToken);

        return OrganizationChange.Success;
    }

    // Names appear in URL paths (/admin/organizations/{name}), so these cannot be in one.
    private const string UnsafeNameCharacters = "/\\?#%";

    /// <summary>
    /// Delete an organization and its memberships. Refused while it owns a package ID in any repository: the
    /// IDs would be left without an owner. (Remove it as an owner first, or hand the IDs over.) The permission grants and
    /// project roles that name the organization go with it, in the same save as the deletion, and an access audit entry
    /// (<c>organization.delete</c>) records it; <paramref name="actor"/> is the user name that did it.
    /// </summary>
    public async Task<OrganizationChange> DeleteOrganizationAsync(string? name, CancellationToken cancellationToken, string? actor = null)
    {
        var lower = name?.ToLowerInvariant();
        #pragma warning disable CS8602, CS8604
        var organization = await _context.Organizations.FirstOrDefaultAsync(o => o.Name.ToLower() == lower, cancellationToken);
        #pragma warning restore CS8602, CS8604
        if (organization == null) return OrganizationChange.NotFound;

        if (await CountOwnedPackageIdsAsync(organization.Name, cancellationToken) > 0) return OrganizationChange.OwnsPackages;

        #pragma warning disable CS8602, CS8604
        var members = await _context.OrganizationMembers.Where(m => m.OrganizationKey == organization.Key).ToListAsync(cancellationToken);
        #pragma warning restore CS8602, CS8604
        _context.OrganizationMembers.RemoveRange(members);
        var cleaned = await AccessAdministration.StageCleanupAsync(_context, PrincipalType.Organization, organization.Key.ToString(), cancellationToken);
        _context.Organizations.Remove(organization);
        if (_context.AccessAuditEntries != null)
        {
            AccessAdministration.Audit(_context, _time.UtcNow, actor, "organization.delete", organization.Name, new { members = members.Count, accessRows = cleaned });
        }

        await _context.SaveChangesAsync(cancellationToken);

        return OrganizationChange.Success;
    }

    /// <summary>
    /// Make a member an administrator of the organization, or not. The last administrator cannot be demoted.
    /// </summary>
    public async Task<OrganizationChange> SetMemberAdminAsync(string organizationName, string userId, bool isAdmin, CancellationToken cancellationToken)
    {
        var name = organizationName.ToLowerInvariant();
        #pragma warning disable CS8602, CS8604
        var members = await _context.OrganizationMembers
            .Where(m => m.Organization != null && m.Organization.Name.ToLower() == name)
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8602, CS8604

        var member = members.FirstOrDefault(m => m.UserId == userId);
        if (member == null) return OrganizationChange.NotFound;
        if (member.IsAdmin && !isAdmin && members.Count(m => m.IsAdmin) == 1) return OrganizationChange.LastAdministrator;

        member.IsAdmin = isAdmin;
        await _context.SaveChangesAsync(cancellationToken);

        return OrganizationChange.Success;
    }
}