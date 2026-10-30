using System;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Database.Sqlite;

/// <summary>
/// Opens a <see cref="SqliteContext"/> on any Sqlite connection string; see <see cref="IDatabaseContextFactory"/>.
/// </summary>
public class SqliteDatabaseContextFactory : IDatabaseContextFactory
{
    /// <inheritdoc/>
    public string DatabaseType => "Sqlite";

    /// <inheritdoc/>
    public IContext Create(
        string connectionString,
        DatabaseOptions? options = null,
        Action<DbContextOptionsBuilder>? configure = null)
    {
        var builder = new DbContextOptionsBuilder<SqliteContext>();

        Configure(builder, connectionString, options?.MySqlServerVersion);
        configure?.Invoke(builder);

        return new SqliteContext(builder.Options);
    }

    /// <summary>
    /// The one place the provider is configured: used by both this factory and the application's
    /// registration, so a second database is configured exactly like the main one.
    /// </summary>
    internal static void Configure(DbContextOptionsBuilder options, string? connectionString, string? mySqlServerVersion)
    {
        options.UseSqlite(connectionString);
    }
}
