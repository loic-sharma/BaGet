using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BaGet.Core
{
    public class NullContext : IContext
    {
        public DatabaseFacade Database => throw new NotImplementedException();

        public bool SupportsLimitInSubqueries => throw new NotImplementedException();
        public IQueryable<Package> PackagesQueryable { get => throw new NotImplementedException(); }

        public Task RunMigrationsAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task RunCreateDatabaseAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task AddAsync(Package package)
        {
            throw new NotImplementedException();
        }

        public Task RemoveAsync(Package package)
        {
            throw new NotImplementedException();
        }

        public bool IsUniqueConstraintViolationException(Exception exception)
        {
            throw new NotImplementedException();
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
