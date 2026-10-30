using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NuGet.Versioning;

namespace BaGet.Core;

public class PackageDatabase : IPackageDatabase
{
    private const int MaxUpdateAttempts = 3;

    private readonly IContext _context;

    public PackageDatabase(IContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<PackageAddResult> AddAsync(Package package, CancellationToken cancellationToken)
    {
        try
        {
            _context.Packages?.Add(package);

            await _context.SaveChangesAsync(cancellationToken);

            return PackageAddResult.Success;
        }
        catch (DbUpdateException e)
            when (_context.IsUniqueConstraintViolationException(e))
        {
            return PackageAddResult.PackageAlreadyExists;
        }
    }

    public async Task<bool> ExistsAsync(string? id, CancellationToken cancellationToken)
    {
        #pragma warning disable CS8604
        return await _context
            .Packages
            .Where(p => p.Id == id)
            .AnyAsync(cancellationToken);
        #pragma warning restore CS8604
    }

    public async Task<bool> ExistsAsync(string? id, NuGetVersion version, CancellationToken cancellationToken)
    {
        #pragma warning disable CS8604
        return await _context
            .Packages
            .Where(p => p.Id == id)
            .Where(p => p.NormalizedVersionString == version.ToNormalizedString())
            .AnyAsync(cancellationToken);
        #pragma warning restore CS8604
    }

    public async Task<IReadOnlyList<Package>> FindManyAsync(
        IReadOnlyCollection<string> ids,
        bool includeUnlisted,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new List<Package>();
        }

        var idList = ids.ToList();
        #pragma warning disable CS8604
        var query = _context.Packages
            .Include(p => p.Dependencies)
            .Include(p => p.PackageTypes)
            .Include(p => p.TargetFrameworks)
            .Include(p => p.Deprecation)
            .Include(p => p.Vulnerabilities)
            .AsSplitQuery()
            .Where(p => idList.Contains(p.Id!));
        #pragma warning restore CS8604

        if (!includeUnlisted)
        {
            query = query.Where(p => p.Listed);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Package>> FindAsync(string? id, bool includeUnlisted, CancellationToken cancellationToken)
    {
        #pragma warning disable CS8604
        var query = _context.Packages
            .Include(p => p.Dependencies)
            .Include(p => p.PackageTypes)
            .Include(p => p.TargetFrameworks)
            .Include(p => p.Deprecation)
            .Include(p => p.Vulnerabilities)
            .AsSplitQuery()
            .Where(p => p.Id == id);
        #pragma warning restore CS8604

        if (!includeUnlisted)
        {
            query = query.Where(p => p.Listed);
        }

        return (await query.ToListAsync(cancellationToken)).AsReadOnly();
    }

    public Task<Package?> FindOrNullAsync(
        string? id,
        NuGetVersion version,
        bool includeUnlisted,
        CancellationToken cancellationToken)
    {
        #pragma warning disable CS8604
        var query = _context.Packages
            .Include(p => p.Dependencies)
            .Include(p => p.TargetFrameworks)
            .Include(p => p.Deprecation)
            .Include(p => p.Vulnerabilities)
            .AsSplitQuery()
            .Where(p => p.Id == id)
            .Where(p => p.NormalizedVersionString == version.ToNormalizedString());
        #pragma warning restore CS8604

        if (!includeUnlisted)
        {
            query = query.Where(p => p.Listed);
        }

        return query.FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> UnlistPackageAsync(string? id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return TryUpdatePackageAsync(id, version, p => p.Listed = false, cancellationToken);
    }

    public Task<bool> RelistPackageAsync(string? id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return TryUpdatePackageAsync(id, version, p => p.Listed = true, cancellationToken);
    }

    public async Task AddDownloadAsync(string? id, NuGetVersion version, CancellationToken cancellationToken)
    {
        try
        {
            await TryUpdatePackageAsync(id, version, p => p.Downloads += 1, cancellationToken, bestEffort: true);
        }
        catch (MaintenanceException)
        {
            // The counter is best effort: a read-only database must not stop a package from being served.
        }
    }

    public async Task<bool> HardDeletePackageAsync(string? id, NuGetVersion version, CancellationToken cancellationToken)
    {
        #pragma warning disable CS8604
        var package = await _context.Packages
            .Where(p => p.Id == id)
            .Where(p => p.NormalizedVersionString == version.ToNormalizedString())
            .Include(p => p.Dependencies)
            .Include(p => p.TargetFrameworks)
            .Include(p => p.Deprecation)
            .Include(p => p.Vulnerabilities)
            .AsSplitQuery()
            .FirstOrDefaultAsync(cancellationToken);
        #pragma warning restore CS8604

        if (package == null)
        {
            return false;
        }

        // The package to dependency relationship is optional in the database, so EF would
        // null the foreign key and leave these rows behind rather than delete them.
        #pragma warning disable CS8604
        _context.PackageDependencies?.RemoveRange(package.Dependencies);
        #pragma warning restore CS8604

        // The same holds for a package's deprecation and vulnerabilities.
        if (package.Deprecation != null)
        {
            _context.PackageDeprecations?.Remove(package.Deprecation);
        }

        #pragma warning disable CS8604
        _context.PackageVulnerabilities?.RemoveRange(package.Vulnerabilities);
        #pragma warning restore CS8604
        _context.Packages.Remove(package);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> SetDeprecationAsync(
        string? id,
        NuGetVersion version,
        PackageDeprecation deprecation,
        CancellationToken cancellationToken)
    {
        if (deprecation == null) throw new ArgumentNullException(nameof(deprecation));

        var package = await FindForUpdateAsync(id, version, cancellationToken);
        if (package == null)
        {
            return false;
        }

        // A version has at most one deprecation, so setting one replaces the last.
        if (package.Deprecation != null)
        {
            _context.PackageDeprecations?.Remove(package.Deprecation);
        }

        package.Deprecation = deprecation;
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> RemoveDeprecationAsync(
        string? id,
        NuGetVersion version,
        CancellationToken cancellationToken)
    {
        var package = await FindForUpdateAsync(id, version, cancellationToken);
        if (package == null)
        {
            return false;
        }

        if (package.Deprecation != null)
        {
            _context.PackageDeprecations?.Remove(package.Deprecation);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<bool> SetVulnerabilitiesAsync(
        string? id,
        NuGetVersion version,
        IReadOnlyList<PackageVulnerability> vulnerabilities,
        CancellationToken cancellationToken)
    {
        if (vulnerabilities == null) throw new ArgumentNullException(nameof(vulnerabilities));

        var package = await FindForUpdateAsync(id, version, cancellationToken);
        if (package == null)
        {
            return false;
        }

        // The list replaces the entries set by hand, so an empty list clears them; what the vulnerability scanner recorded
        // (Source set) is the scanner's and stays until the next scan.
        var manual = (package.Vulnerabilities ?? new List<PackageVulnerability>()).Where(v => v.Source == null).ToList();
        #pragma warning disable CS8604
        _context.PackageVulnerabilities?.RemoveRange(manual);
        #pragma warning restore CS8604
        var scanned = (package.Vulnerabilities ?? new List<PackageVulnerability>()).Where(v => v.Source != null);
        foreach (var vulnerability in vulnerabilities) vulnerability.Source = null;
        package.Vulnerabilities = scanned.Concat(vulnerabilities).ToList();
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<PackageVulnerability>> GetAllVulnerabilitiesAsync(
        CancellationToken cancellationToken)
    {
        #pragma warning disable CS8604
        return await _context.PackageVulnerabilities
            .Include(v => v.Package)
            .Where(v => v.Package != null)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8604
    }

    private Task<Package?> FindForUpdateAsync(
        string? id,
        NuGetVersion version,
        CancellationToken cancellationToken)
    {
        #pragma warning disable CS8604
        return _context.Packages
            .Include(p => p.Deprecation)
            .Include(p => p.Vulnerabilities)
            .AsSplitQuery()
            .Where(p => p.Id == id)
            .Where(p => p.NormalizedVersionString == version.ToNormalizedString())
            .FirstOrDefaultAsync(cancellationToken);
        #pragma warning restore CS8604
    }

    /// <summary>
    /// Rows carry a concurrency token on SQL Server and MySQL, so a save racing another writer of the same row
    /// (a download, an unlist) throws <see cref="DbUpdateConcurrencyException"/>. The change is re-applied to a
    /// fresh copy of the row up to <see cref="MaxUpdateAttempts"/> times; then the exception is rethrown, or
    /// dropped when <paramref name="bestEffort"/> (the download counter) is set.
    /// </summary>
    private async Task<bool> TryUpdatePackageAsync(
        string? id,
        NuGetVersion version,
        Action<Package> action,
        CancellationToken cancellationToken,
        bool bestEffort = false)
    {
        for (var attempt = 1; ; attempt++)
        {
            #pragma warning disable CS8604
            var package = await _context.Packages
                .Where(p => p.Id == id)
                .Where(p => p.NormalizedVersionString == version.ToNormalizedString())
                .FirstOrDefaultAsync(cancellationToken);
            #pragma warning restore CS8604

            if (package == null)
            {
                return false;
            }

            action(package);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);

                return true;
            }
            catch (DbUpdateConcurrencyException e)
            {
                foreach (var entry in e.Entries)
                {
                    entry.State = EntityState.Detached;
                }

                if (attempt < MaxUpdateAttempts)
                {
                    continue;
                }

                if (bestEffort)
                {
                    return false;
                }

                throw;
            }
        }
    }
}
