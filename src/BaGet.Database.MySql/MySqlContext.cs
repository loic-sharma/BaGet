using System;
using System.Linq;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace BaGet.Database.MySql
{
    public class MySqlContext : AbstractContext<MySqlContext>
    {
        /// <summary>
        /// Max string length for mapping to varchar(N)
        /// It string length will be greater than that value, it would be mapped to a longtext
        /// </summary>
        private const int MaxAllowedVarcharLength = 256;

        /// <summary>
        /// The MySQL Server error code for when a unique constraint is violated.
        /// </summary>
        private const int UniqueConstraintViolationErrorCode = 1062;

        public MySqlContext(DbContextOptions<MySqlContext> options)
            : base(options)
        {
        }

        /// <summary>
        /// MySQL does not support LIMIT clauses in subqueries for certain subquery operators.
        /// See: https://dev.mysql.com/doc/refman/8.0/en/subquery-restrictions.html
        /// </summary>
        public override bool SupportsLimitInSubqueries => false;

        public override bool IsUniqueConstraintViolationException(DbUpdateException exception) =>
            exception.InnerException is MySqlException { Number: UniqueConstraintViolationErrorCode };

        /// <summary>
        /// MySQL has a limit of row size - 65535 bytes
        /// So we map string, string[] and URI, which are longer than 256 symbols to longtext MySQL type
        /// See: https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql/issues/1606
        /// </summary>
        /// <param name="builder">EF Core model builder</param>
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            var longtextProperties = builder.Model
                .GetEntityTypes()
                .SelectMany(e => e.GetProperties())
                .Where(e =>
                    (e.ClrType == typeof(string) || e.ClrType == typeof(string[]) || e.ClrType == typeof(Uri)) &&
                    e.GetMaxLength() > MaxAllowedVarcharLength);

            foreach (var property in longtextProperties)
            {
                property.SetMaxLength(null);
            }
        }
    }
}
