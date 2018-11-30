using BaGet.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BaGet.Entities
{
    public class PostgresSqlContext : AbstractContext<PostgresSqlContext>
    {
        /// <summary>
        /// The error code for when a unique constraint is violated.
        /// https://www.postgresql.org/docs/9.6/errcodes-appendix.html
        /// </summary>
        private const int UniqueViolation = 23505;

        public PostgresSqlContext(DbContextOptions<PostgresSqlContext> options)
            : base(options)
        {
        }

        public override bool IsUniqueConstraintViolationException(DbUpdateException exception)
        {
            return exception.InnerException is PostgresException postgresException &&
                int.TryParse(postgresException.SqlState, out var code) &&
                code == UniqueViolation;
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.HasPostgresExtension("citext");

            builder.Entity<Package>()
                .Property(p => p.Id)
                .HasColumnType("citext");

            builder.Entity<PackageDependency>()
                .Property(p => p.Id)
                .HasColumnType("citext");
        }
    }
}
