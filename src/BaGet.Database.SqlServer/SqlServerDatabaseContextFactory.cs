using System;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Database.SqlServer;

/// <summary>
/// Opens a <see cref="SqlServerContext"/> on any SqlServer connection string; see <see cref="IDatabaseContextFactory"/>.
/// </summary>
public class SqlServerDatabaseContextFactory : IDatabaseContextFactory
{
    /// <inheritdoc/>
    public string DatabaseType => "SqlServer";

    /// <inheritdoc/>
    public IContext Create(
        string connectionString,
        DatabaseOptions? options = null,
        Action<DbContextOptionsBuilder>? configure = null)
    {
        var builder = new DbContextOptionsBuilder<SqlServerContext>();

        Configure(builder, connectionString, options?.MySqlServerVersion);
        configure?.Invoke(builder);

        return new SqlServerContext(builder.Options);
    }

    /// <summary>
    /// The one place the provider is configured: used by both this factory and the application's
    /// registration, so a second database is configured exactly like the main one.
    /// </summary>
    internal static void Configure(DbContextOptionsBuilder options, string? connectionString, string? mySqlServerVersion)
    {
        options.UseSqlServer(connectionString);
    }
}
