using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace BaGet.Database.MySql;

public class MySqlContext : AbstractContext<MySqlContext>
{
    /// <summary>
    /// The MySQL Server error code for when a unique constraint is violated.
    /// </summary>
    private const int UniqueConstraintViolationErrorCode = 1062;

    /// <summary>The binary collation of the npm identifier columns.</summary>
    internal const string NpmCollation = "utf8mb4_bin";

    public MySqlContext(DbContextOptions<MySqlContext> options) : base(options)
    {
    }

    public override bool IsUniqueConstraintViolationException(DbUpdateException exception)
    {
        return exception.InnerException is MySqlException mysqlException &&
               mysqlException.Number == UniqueConstraintViolationErrorCode;
    }

    /// <summary>
    /// MySQL does not support LIMIT clauses in subqueries for certain subquery operators.
    /// See: https://dev.mysql.com/doc/refman/8.0/en/subquery-restrictions.html
    /// </summary>
    public override bool SupportsLimitInSubqueries => false;

    /// <summary>
    /// The long string columns are stored as "longtext" rather than the "varchar(4000)"
    /// that a 4000 character maximum length would otherwise map to.
    /// </summary>
    /// <remarks>
    /// This is what the database has had since the schema was created, and it cannot be
    /// changed: MySQL limits a row to 65535 bytes, and utf8mb4 needs up to four bytes a
    /// character, so a single varchar(4000) column takes 16000 bytes and these eight
    /// together are far beyond the limit. Declaring the type here keeps the model and
    /// the database in agreement, so a migration for an unrelated change does not try to
    /// convert them.
    /// </remarks>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        var package = builder.Entity<Package>();

        package.Property(p => p.Authors).HasColumnType("longtext");
        package.Property(p => p.Description).HasColumnType("longtext");
        package.Property(p => p.IconUrl).HasColumnType("longtext");
        package.Property(p => p.LicenseUrl).HasColumnType("longtext");
        package.Property(p => p.ProjectUrl).HasColumnType("longtext");
        package.Property(p => p.RepositoryUrl).HasColumnType("longtext");
        package.Property(p => p.Summary).HasColumnType("longtext");
        package.Property(p => p.Tags).HasColumnType("longtext");

        // npm package names and dist-tags are case-sensitive (legacy names such as JSONStream exist): compared by code point,
        // never by the default case-insensitive collation, so an exact lookup and the unique index behave as on SQLite and
        // PostgreSQL.
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
}