using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core.Entities
{
    public interface IContext
    {
        DbSet<Package> Packages { get; set; }

        /// <summary>
        /// Check whether a <see cref="DbUpdateException"/> is due to a SQL unique constraint violation.
        /// </summary>
        /// <param name="exception">The exception to inspect.</param>
        /// <returns>Whether the exception was caused to SQL unique constraint violation.</returns>
        bool IsUniqueConstraintViolationException(DbUpdateException exception);

        Task<int> SaveChangesAsync();
    }
}
