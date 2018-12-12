using BaGet.Core.Entities;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core
{
    public interface IContext : IDisposable
    {
        [Obsolete("query")]
        IQueryable<Package> Packages { get; }
        IQueryable<T> Query<T>();

        Task InsertAsync(object entity, CancellationToken cancellationToken = default);
        Task RemoveAsync(object entity, CancellationToken cancellationToken = default);
        Task SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
