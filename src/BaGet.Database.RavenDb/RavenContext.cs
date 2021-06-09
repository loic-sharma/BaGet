using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;

namespace BaGet.Database.RavenDb
{
    public class RavenContext : IContext
    {
        private readonly IAsyncDocumentSession _session;

        public bool SupportsLimitInSubqueries => true;

        public IQueryable<Package> PackagesQueryable => _session.Query<Package>();

        public RavenContext(IAsyncDocumentSession session)
        {
            _session = session;
        }

        public bool IsUniqueConstraintViolationException(Exception exception)
            => false;

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            await _session.SaveChangesAsync(cancellationToken);
            return 0; // TODO: check if it is used anywhere
        }

        public Task RunMigrationsAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RunCreateDatabaseAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task AddAsync(Package package)
            => _session.StoreAsync(package);

        public Task RemoveAsync(Package package)
        {
            _session.Delete(package);
            return Task.CompletedTask;
        }
    }
}
