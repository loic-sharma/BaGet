using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using Microsoft.EntityFrameworkCore;
using NuGet.Versioning;

namespace BaGet.Core.State
{
    public class PackageService : IPackageService
    {
        private readonly IContext _context;

        public PackageService(IContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public virtual async Task<PackageAddResult> AddAsync(Package package)
        {
            try
            {
                _context.Packages.Add(package);

                await _context.SaveChangesAsync();

                return PackageAddResult.Success;
            }
            catch (DbUpdateException e)
                when (_context.IsUniqueConstraintViolationException(e))
            {
                return PackageAddResult.PackageAlreadyExists;
            }
        }

        public virtual Task<bool> ExistsAsync(string id, NuGetVersion version = null)
        {
            var query = _context.Packages.Where(p => p.Id == id);

            if (version != null)
            {
                query = query.Where(p => p.VersionString == version.ToNormalizedString());
            }

            return query.AnyAsync();
        }

        public virtual async Task<IReadOnlyList<Package>> FindAsync(string id, bool includeUnlisted = false)
        {
            var query = _context.Packages
                .Include(p => p.Dependencies)
                .Include(p => p.PackageTypes)
                .Include(p => p.TargetFrameworks)
                .Where(p => p.Id == id);

            if (!includeUnlisted)
            {
                query = query.Where(p => p.Listed);
            }

            return (await query.ToListAsync()).AsReadOnly();
        }

        public virtual Task<Package> FindOrNullAsync(string id, NuGetVersion version, bool includeUnlisted = false)
        {
            var query = _context.Packages
                .Include(p => p.Dependencies)
                .Include(p => p.TargetFrameworks)
                .Where(p => p.Id == id)
                .Where(p => p.VersionString == version.ToNormalizedString());

            if (!includeUnlisted)
            {
                query = query.Where(p => p.Listed);
            }

            return query.FirstOrDefaultAsync();
        }

        public virtual Task<bool> UnlistPackageAsync(string id, NuGetVersion version)
        {
            return TryUpdatePackageAsync(id, version, p => p.Listed = false);
        }

        public virtual Task<bool> RelistPackageAsync(string id, NuGetVersion version)
        {
            return TryUpdatePackageAsync(id, version, p => p.Listed = true);
        }

        public virtual Task<bool> AddDownloadAsync(string id, NuGetVersion version)
        {
            return TryUpdatePackageAsync(id, version, p => p.Downloads += 1);
        }

        public virtual async Task<bool> HardDeletePackageAsync(string id, NuGetVersion version)
        {
            var package = await _context.Packages
                .Where(p => p.Id == id)
                .Where(p => p.VersionString == version.ToNormalizedString())
                .Include(p => p.Dependencies)
                .Include(p => p.TargetFrameworks)
                .FirstOrDefaultAsync();

            if (package == null)
            {
                return false;
            }

            _context.Packages.Remove(package);
            await _context.SaveChangesAsync();

            return true;
        }

        protected virtual async Task<bool> TryUpdatePackageAsync(string id, NuGetVersion version, Action<Package> action)
        {
            var package = await _context.Packages
                .Where(p => p.Id == id)
                .Where(p => p.VersionString == version.ToNormalizedString())
                .FirstOrDefaultAsync();

            if (package != null)
            {
                action(package);
                await _context.SaveChangesAsync();

                return true;
            }

            return false;
        }
    }
}
