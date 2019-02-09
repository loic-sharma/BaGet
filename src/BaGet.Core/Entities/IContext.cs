using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BaGet.Core.Entities
{
    public interface IContext
    {
        DatabaseFacade Database { get; }

        DbSet<Package> Packages { get; set; }

        /// <summary>
        /// Check whether a <see cref="DbUpdateException"/> is due to a SQL unique constraint violation.
        /// </summary>
        /// <param name="exception">The exception to inspect.</param>
        /// <returns>Whether the exception was caused to SQL unique constraint violation.</returns>
        bool IsUniqueConstraintViolationException(DbUpdateException exception);

        /// <summary>
        /// Whether this database engine supports LINQ "Take" in subqueries.
        /// </summary>
        bool SupportsLimitInSubqueries { get; }

        Task<int> SaveChangesAsync();
    }
}
