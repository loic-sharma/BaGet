using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core.Extensions
{
    public static class ExceptionExtensions
    {
        /// <summary>
        /// The Sqlite error code for when a unique constraint is violated.
        /// </summary>
        private const int SqliteUniqueConstraintViolationErrorCode = 19;

        /// <summary>
        /// Check whether a <see cref="DbUpdateException"/> is due to a SQL unique constraint violation.
        /// </summary>
        /// <param name="exception">The exception to inspect.</param>
        /// <returns>Whether the exception was caused to SQL unique constraint violation.</returns>
        public static bool IsUniqueConstraintViolationException(this DbUpdateException exception)
        {
            // TODO: Support more databases!
            if (exception.InnerException is SqliteException sqliteException)
            {
                return sqliteException.SqliteErrorCode == SqliteUniqueConstraintViolationErrorCode;
            }

            return false;
        }
    }
}
