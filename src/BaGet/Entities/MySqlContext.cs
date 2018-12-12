using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace BaGet.Entities
{
    public class MySqlContext : AbstractContext<MySqlContext>
    {
        /// <summary>
        /// The MySQL Server error code for when a unique constraint is violated.
        /// </summary>
        private const int UniqueConstraintViolationErrorCode = 1062;

        public MySqlContext(DbContextOptions<MySqlContext> options) : base(options)
        {
        }

        public override bool IsUniqueConstraintViolationException(DbUpdateException exception)
        {
            return exception.InnerException is MySqlException mysqlException &&
                   mysqlException.Number == UniqueConstraintViolationErrorCode;
        }
    }
}
