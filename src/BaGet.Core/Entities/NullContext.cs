using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BaGet.Core
{
    public class NullContext : IContext
    {
        public DatabaseFacade Database => throw new NotImplementedException();

        public DbSet<Package> Packages { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public bool SupportsLimitInSubqueries => throw new NotImplementedException();

        public bool IsUniqueConstraintViolationException(DbUpdateException exception)
        {
            throw new NotImplementedException();
        }

        public Task RunMigrationsAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
