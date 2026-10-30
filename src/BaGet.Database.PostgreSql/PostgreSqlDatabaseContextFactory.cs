using System;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Database.PostgreSql;

/// <summary>
/// Opens a <see cref="PostgreSqlContext"/> on any PostgreSql connection string; see <see cref="IDatabaseContextFactory"/>.
/// </summary>
public class PostgreSqlDatabaseContextFactory : IDatabaseContextFactory
{
    /// <inheritdoc/>
    public string DatabaseType => "PostgreSql";

    /// <inheritdoc/>
    public IContext Create(
        string connectionString,
        DatabaseOptions? options = null,
        Action<DbContextOptionsBuilder>? configure = null)
    {
        var builder = new DbContextOptionsBuilder<PostgreSqlContext>();

        Configure(builder, connectionString, options?.MySqlServerVersion);
        configure?.Invoke(builder);

        return new PostgreSqlContext(builder.Options);
    }

    /// <summary>
    /// The one place the provider is configured: used by both this factory and the application's
    /// registration, so a second database is configured exactly like the main one.
    /// </summary>
    internal static void Configure(DbContextOptionsBuilder options, string? connectionString, string? mySqlServerVersion)
    {
        options.UseNpgsql(connectionString);
    }
}
