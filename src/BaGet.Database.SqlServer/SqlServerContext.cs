using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BaGet.Database.SqlServer;

public class SqlServerContext : AbstractContext<SqlServerContext>
{
    /// <summary>
    /// The SQL Server error code for a violated UNIQUE or PRIMARY KEY constraint.
    /// </summary>
    private const int UniqueConstraintViolationErrorCode = 2627;

    /// <summary>
    /// The SQL Server error code for a violated unique index. EF Core creates unique indexes (not constraints) for
    /// <c>HasIndex(...).IsUnique()</c>, such as the one on a package's repository, ID and version, so this is the code a
    /// duplicate package raises.
    /// </summary>
    private const int UniqueIndexViolationErrorCode = 2601;

    public SqlServerContext(DbContextOptions<SqlServerContext> options)
        : base(options)
    { }

    /// <summary>
    /// npm package names and dist-tags are case-sensitive (legacy names such as <c>JSONStream</c> still exist), so they are
    /// compared by code point, never by the database's default case-insensitive collation: an exact lookup, and the unique
    /// index, then behave as on SQLite and PostgreSQL.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<NpmPackage>().Property(p => p.Name).UseCollation(NpmCollation);
        builder.Entity<NpmDistTag>().Property(t => t.Tag).UseCollation(NpmCollation);

        // Maven coordinates, versions and file names are case-sensitive folders, compared by code point for the same reason.
        builder.Entity<MavenArtifact>().Property(a => a.GroupId).UseCollation(NpmCollation);
        builder.Entity<MavenArtifact>().Property(a => a.ArtifactId).UseCollation(NpmCollation);
        builder.Entity<MavenVersion>().Property(v => v.Version).UseCollation(NpmCollation);
        builder.Entity<MavenFile>().Property(f => f.FileName).UseCollation(NpmCollation);

        // Tags are case-sensitive (image names and digests are lower case by rule).
        builder.Entity<OciTag>().Property(t => t.Name).UseCollation(NpmCollation);
    }

    /// <summary>The binary collation of the npm identifier columns.</summary>
    internal const string NpmCollation = "Latin1_General_100_BIN2";

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
                .Any(error => error.Number == UniqueConstraintViolationErrorCode || error.Number == UniqueIndexViolationErrorCode);
        }

        return false;
    }

    /// <summary>
    /// Turns <c>IDENTITY_INSERT</c> on for the table, so explicit key values can be inserted.
    /// </summary>
    /// <remarks>
    /// The setting belongs to the connection, so this opens the context's connection and keeps it open
    /// (EF reference-counts it, so <c>SaveChanges</c> reuses the same session) until
    /// <see cref="EndKeyedInsertAsync"/>. Only one table per session can have it on. Tables without an
    /// identity column (such as Identity's string-keyed ones) are left alone.
    /// </remarks>
    public override async Task BeginKeyedInsertAsync(IEntityType table, CancellationToken cancellationToken)
    {
        var sql = BuildIdentityInsertSql(table, on: true);
        if (sql == null)
        {
            return;
        }

        await Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
        catch
        {
            await Database.CloseConnectionAsync();
            throw;
        }
    }

    /// <summary>
    /// Turns <c>IDENTITY_INSERT</c> off again and releases the connection opened by
    /// <see cref="BeginKeyedInsertAsync"/>. SQL Server continues the identity after the highest inserted value itself.
    /// </summary>
    public override async Task EndKeyedInsertAsync(IEntityType table, CancellationToken cancellationToken)
    {
        var sql = BuildIdentityInsertSql(table, on: false);
        if (sql == null)
        {
            return;
        }

        try
        {
            await Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
        finally
        {
            await Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// The <c>SET IDENTITY_INSERT</c> statement for the table, or <see langword="null"/> when none of its
    /// columns is an identity column.
    /// </summary>
    internal static string? BuildIdentityInsertSql(IEntityType table, bool on)
    {
        var name = table.GetTableName();
        if (name == null)
        {
            return null;
        }

        var schema = table.GetSchema();
        var store = StoreObjectIdentifier.Table(name, schema);
        var hasIdentity = table.GetProperties().Any(p =>
            p.GetValueGenerationStrategy(store) == SqlServerValueGenerationStrategy.IdentityColumn);

        if (!hasIdentity)
        {
            return null;
        }

        var qualified = schema == null ? Quote(name) : $"{Quote(schema)}.{Quote(name)}";
        return $"SET IDENTITY_INSERT {qualified} {(on ? "ON" : "OFF")}";
    }

    private static string Quote(string identifier) => "[" + identifier.Replace("]", "]]") + "]";
}