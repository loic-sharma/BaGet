using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core.Services
{
    public class DatabaseSourceCodeService : ISourceCodeService
    {
        private readonly IContext _context;

        public DatabaseSourceCodeService(IContext context)
        {
            _context = context;
        }

        public async Task<SourceCodeAddResult> IndexAsync(Package package, IEnumerable<SourceCodeAssembly> sourceCodes, CancellationToken cancellationToken)
        {
            try
            {
                foreach (var assembly in sourceCodes)
                {
                    await _context.SourceCodeAssemblies.AddAsync(assembly);

                    assembly.PackageKey = package.Key;
                }

                await _context.SaveChangesAsync();

                return SourceCodeAddResult.Success;
            }
            catch (DbUpdateException e) when (_context.IsUniqueConstraintViolationException(e))
            {
                return SourceCodeAddResult.PackageAlreadyExists;
            }
        }
    }
}
