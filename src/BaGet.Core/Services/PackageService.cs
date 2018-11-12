using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using Microsoft.EntityFrameworkCore;
using NuGet.Versioning;

namespace BaGet.Core.Services
{
    public class PackageService : IPackageService
    {
        private readonly IContext _context;

        public PackageService(IContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<PackageAddResult> AddAsync(Package package)
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

        public Task<bool> ExistsAsync(string id, NuGetVersion version = null)
        {
            var query = _context.Packages.Where(p => p.Id == id);

            if (version != null)
            {
                query = query.Where(p => p.VersionString == version.ToNormalizedString());
            }

            return query.AnyAsync();
        }

        public async Task<IReadOnlyList<Package>> FindAsync(string id, bool includeUnlisted = false)
        {
            var query = _context.Packages.Include(a => a.Dependencies).Where(p => p.Id == id);

            if (!includeUnlisted)
            {
                query = query.Where(p => p.Listed);
            }

            return (await query.ToListAsync()).AsReadOnly();
        }

        public Task<Package> FindOrNullAsync(string id, NuGetVersion version, bool includeUnlisted = false)
        {
            var query = _context.Packages
                .Include(a => a.Dependencies)
                .Where(p => p.Id == id)
                .Where(p => p.VersionString == version.ToNormalizedString());

            if (!includeUnlisted)
            {
                query = query.Where(p => p.Listed);
            }

            return query.FirstOrDefaultAsync();
        }

        public Task<bool> UnlistPackageAsync(string id, NuGetVersion version)
        {
            return TryUpdatePackageAsync(id, version, p => p.Listed = false);
        }

        public Task<bool> RelistPackageAsync(string id, NuGetVersion version)
        {
            return TryUpdatePackageAsync(id, version, p => p.Listed = true);
        }

        public Task<bool> AddDownloadAsync(string id, NuGetVersion version)
        {
            return TryUpdatePackageAsync(id, version, p => p.Downloads += 1);
        }

        public async Task<bool> HardDeletePackageAsync(string id, NuGetVersion version)
        {
            var package = await _context.Packages
                .Where(p => p.Id == id)
                .Where(p => p.VersionString == version.ToNormalizedString())
                .Include(p => p.Dependencies)
                .FirstOrDefaultAsync();

            if (package == null)
            {
                return false;
            }

            _context.Packages.Remove(package);
            await _context.SaveChangesAsync();

            return true;
        }

        private async Task<bool> TryUpdatePackageAsync(string id, NuGetVersion version, Action<Package> action)
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
