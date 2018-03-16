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
        private readonly BaGetContext _context;

        public PackageService(BaGetContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public Task<bool> ExistsAsync(string id, NuGetVersion version)
            => _context.Packages
                .Where(p => p.Id == id)
                .Where(p => p.Version == version)
                .AnyAsync();

        public async Task<IReadOnlyList<Package>> FindAsync(string id)
        {
            var results = await _context.Packages
                .Where(p => p.Id == id)
                .ToListAsync();

            return results.AsReadOnly();
        }

        public Task<Package> FindAsync(string id, NuGetVersion version)
            => _context.Packages
                .Where(p => p.Id == id)
                .Where(p => p.Version == version)
                .FirstOrDefaultAsync();
    }
}
