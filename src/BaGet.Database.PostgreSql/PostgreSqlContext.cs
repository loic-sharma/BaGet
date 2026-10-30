using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace BaGet.Database.PostgreSql;

public class PostgreSqlContext : AbstractContext<PostgreSqlContext>
{
    /// <summary>
    /// The PostgreSql error code for when a unique constraint is violated.
    /// See: https://www.postgresql.org/docs/9.6/errcodes-appendix.html
    /// </summary>
    private const int UniqueConstraintViolationErrorCode = 23505;

    public PostgreSqlContext(DbContextOptions<PostgreSqlContext> options)
        : base(options)
    {
    }

    public override bool IsUniqueConstraintViolationException(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException &&
               int.TryParse(postgresException.SqlState, out var code) &&
               code == UniqueConstraintViolationErrorCode;
    }

    public override async Task RunMigrationsAsync(CancellationToken cancellationToken)
    {
        await base.RunMigrationsAsync(cancellationToken);

        // Npgsql caches the database's type information on the initial connection.
        // This causes issues when BaGet creates the database as it may add the citext
        // extension to support case insensitive columns.
        // See: https://github.com/loic-sharma/BaGet/issues/442
        // See: https://github.com/npgsql/efcore.pg/issues/170#issuecomment-303417225
        if (Database.GetDbConnection() is NpgsqlConnection connection)
        {
            // Migrations may run more than once on one context (a data copy checks the target, then migrates
            // it), and the first run can leave the connection open: open it only when it is closed.
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            connection.ReloadTypes();
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasPostgresExtension("citext");

        builder.Entity<Package>()
            .Property(p => p.Id)
            .HasColumnType("citext");

        builder.Entity<Package>()
            .Property(p => p.NormalizedVersionString)
            .HasColumnType("citext");

        builder.Entity<PackageDependency>()
            .Property(p => p.Id)
            .HasColumnType("citext");

        builder.Entity<PackageType>()
            .Property(p => p.Name)
            .HasColumnType("citext");

        builder.Entity<TargetFramework>()
            .Property(p => p.Moniker)
            .HasColumnType("citext");
    }

    /// <summary>
    /// Moves each serial/identity sequence of the table past the highest key that was inserted explicitly.
    /// </summary>
    /// <remarks>
    /// Explicit keys do not advance a PostgreSQL sequence, so without this the next generated key would
    /// collide with a copied row. Tables with no serial/identity key (such as Identity's string-keyed
    /// ones) are left alone.
    /// </remarks>
    public override async Task EndKeyedInsertAsync(IEntityType table, CancellationToken cancellationToken)
    {
        foreach (var sql in BuildSetValSql(table))
        {
            await Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
    }

    /// <summary>
    /// The <c>setval</c> statements for the table's serial/identity columns; empty when it has none.
    /// An empty table leaves the sequence at its start (<c>is_called</c> false), otherwise the next value is MAX + 1.
    /// </summary>
    internal static string[] BuildSetValSql(IEntityType table)
    {
        var name = table.GetTableName();
        if (name == null)
        {
            return System.Array.Empty<string>();
        }

        var schema = table.GetSchema();
        var store = StoreObjectIdentifier.Table(name, schema);
        var qualified = schema == null ? Quote(name) : $"{Quote(schema)}.{Quote(name)}";

        return table.GetProperties()
            .Where(p => p.GetValueGenerationStrategy(store) is
                NpgsqlValueGenerationStrategy.SerialColumn or
                NpgsqlValueGenerationStrategy.IdentityByDefaultColumn or
                NpgsqlValueGenerationStrategy.IdentityAlwaysColumn)
            .Select(p =>
            {
                var column = p.GetColumnName(store)!;

                // pg_get_serial_sequence parses its table argument as an identifier (so it is quoted
                // inside the literal) but takes the column name verbatim.
                return $"SELECT setval(pg_get_serial_sequence('{Literal(qualified)}', '{Literal(column)}'), " +
                       $"COALESCE(MAX({Quote(column)}), 1), MAX({Quote(column)}) IS NOT NULL) FROM {qualified}";
            })
            .ToArray();
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static string Literal(string text) => text.Replace("'", "''");
}