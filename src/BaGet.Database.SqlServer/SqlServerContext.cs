using System.Linq;
using BaGet.Core;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Database.SqlServer
{
    public class SqlServerContext : AbstractContext<SqlServerContext>
    {
        /// <summary>
        /// The SQL Server error code for when a unique contraint is violated.
        /// </summary>
        private const int UniqueConstraintViolationErrorCode = 2627;

        public SqlServerContext(DbContextOptions<SqlServerContext> options)
            : base(options)
        { }

        /// <summary>
        /// Check whether a <see cref="DbUpdateException"/> is due to a SQL unique constraint violation.
        /// </summary>
        /// <param name="exception">The exception to inspect.</param>
        /// <returns>Whether the exception was caused to SQL unique constraint violation.</returns>
        public override bool IsUniqueConstraintViolationException(DbUpdateException exception)
        {
            if (exception.GetBaseException() is SqlException sqlException)
            {
                return sqlException.Errors
                    .OfType<SqlError>()
                    .Any(error => error.Number == UniqueConstraintViolationErrorCode);
            }

            return false;
        }
    }
}
